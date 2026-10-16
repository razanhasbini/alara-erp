using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Sales;

public sealed record OrderRecipient(string Name, string Phone, string? City, string? Address);

/// <summary>
/// A customer order. Owns its lines (name/type/price snapshots), totals and audit history, and enforces
/// the status and payment state machines. Stock effects of transitions are orchestrated by the
/// application layer through the Inventory module.
/// </summary>
public sealed class Order : IAuditable
{
    private static readonly Dictionary<OrderStatus, OrderStatus[]> AllowedTransitions = new()
    {
        [OrderStatus.Draft] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Draft, OrderStatus.Shipped, OrderStatus.Cancelled],
        [OrderStatus.Shipped] = [OrderStatus.Delivered, OrderStatus.Returned],
        [OrderStatus.Delivered] = [],
        [OrderStatus.Cancelled] = [],
        [OrderStatus.Returned] = [],
    };

    private readonly List<OrderItem> _items = [];
    private readonly List<OrderHistoryEntry> _history = [];

    private Order() { }

    public Order(string orderNumber, OrderSource source, Guid warehouseId, OrderRecipient recipient,
        PaymentMethod paymentMethod, string currencyCode, Guid? createdByUserId, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        OrderNumber = orderNumber;
        Status = OrderStatus.Draft;
        PaymentStatus = PaymentStatus.Unpaid;
        PaymentMethod = paymentMethod;
        Source = source;
        WarehouseId = warehouseId;
        CurrencyCode = currencyCode;
        CreatedByUserId = createdByUserId;
        CreatedAt = createdAt;
        SetRecipient(recipient);
        _history.Add(new OrderHistoryEntry(Id, OrderEventType.Status, null, OrderStatus.Draft.ToString(), "Order created.", createdByUserId, createdAt));
    }

    public Guid Id { get; private set; }
    public string OrderNumber { get; private set; } = "";
    public OrderStatus Status { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public PaymentStatus PaymentStatus { get; private set; }
    public OrderSource Source { get; private set; }
    public Guid WarehouseId { get; private set; }
    public Guid? CustomerId { get; private set; }

    public string RecipientName { get; private set; } = "";
    public string RecipientPhone { get; private set; } = "";
    public string? RecipientCity { get; private set; }
    public string? RecipientAddress { get; private set; }

    public Guid? DeliveryCompanyId { get; private set; }
    public string? TrackingNumber { get; private set; }

    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    /// <summary>Delivery fee charged to the customer.</summary>
    public decimal ShippingTotal { get; private set; }
    /// <summary>What the delivery company charges ÀLara Line; deducted from the cash it remits.</summary>
    public decimal? CourierFee { get; private set; }
    public decimal GrandTotal { get; private set; }
    public string CurrencyCode { get; private set; } = "";

    public Guid? CourierRemittanceId { get; private set; }
    public string? Notes { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderHistoryEntry> History => _history;

    public bool IsFinal => Status is OrderStatus.Delivered or OrderStatus.Cancelled or OrderStatus.Returned;

    // ---- editing -------------------------------------------------------------------------

    public void SetRecipient(OrderRecipient recipient)
    {
        EnsureNotFinal("change the recipient of");
        RecipientName = Required(recipient.Name, "Recipient name");
        RecipientPhone = NormalizePhone(Required(recipient.Phone, "Recipient phone"));
        RecipientCity = Clean(recipient.City);
        RecipientAddress = Clean(recipient.Address);
    }

    public void SetCustomer(Guid customerId)
    {
        EnsureNotFinal("change the customer of");
        CustomerId = customerId;
    }

    public void SetNotes(string? notes) => Notes = Clean(notes);

    public void SetDelivery(Guid? deliveryCompanyId, string? trackingNumber, decimal? courierFee)
    {
        EnsureNotFinal("change delivery details of");
        if (courierFee is { } fee) Guard.NotNegative(fee, "Courier fee");
        DeliveryCompanyId = deliveryCompanyId;
        TrackingNumber = Clean(trackingNumber);
        CourierFee = courierFee is { } f ? Guard.Money(f) : null;
    }

    /// <summary>Lines, discount and delivery fee can only change while the order is a draft (no stock reserved).</summary>
    public void ReplaceLines(IReadOnlyList<OrderItem> items, decimal discountTotal, decimal shippingTotal,
        Guid warehouseId, OrderSource source, PaymentMethod paymentMethod)
    {
        if (Status != OrderStatus.Draft)
            throw new DomainException("orders.not_editable", "Only draft orders can be edited. Reopen the order first.");
        if (items.Count == 0)
            throw new DomainException("orders.no_items", "An order needs at least one item.");

        _items.Clear();
        _items.AddRange(items);
        WarehouseId = warehouseId;
        Source = source;
        PaymentMethod = paymentMethod;

        Subtotal = Guard.Money(_items.Sum(i => i.LineTotal));
        DiscountTotal = Guard.Money(Guard.NotNegative(discountTotal, "Order discount"));
        ShippingTotal = Guard.Money(Guard.NotNegative(shippingTotal, "Delivery fee"));
        if (DiscountTotal > Subtotal)
            throw new DomainException("orders.discount_exceeds_subtotal", "The order discount cannot exceed the items subtotal.");
        GrandTotal = Guard.Money(Subtotal - DiscountTotal + ShippingTotal);
    }

    /// <summary>Units of each individual product this order needs (bundles expanded via their component snapshots).</summary>
    public IReadOnlyDictionary<Guid, int> StockRequirements() =>
        _items.SelectMany(i => i.ProductType == ProductType.Bundle
                ? i.Components.Select(c => (c.ComponentProductId, c.TotalQuantity))
                : [(i.ProductId, i.Quantity)])
            .GroupBy(x => x.Item1)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Item2));

    // ---- state machine -------------------------------------------------------------------

    public static bool CanTransition(OrderStatus from, OrderStatus to) => AllowedTransitions[from].Contains(to);

    public void TransitionTo(OrderStatus target, Guid? userId, DateTimeOffset at, string? notes = null)
    {
        if (!CanTransition(Status, target))
            throw new DomainException("orders.invalid_transition", $"An order cannot go from {Status} to {target}.");
        if (target is OrderStatus.Cancelled or OrderStatus.Returned && PaymentStatus == PaymentStatus.Paid)
            throw new DomainException("orders.paid_cannot_cancel",
                "This order is already paid. Refunds are not supported yet; resolve the payment before cancelling.");
        if (target == OrderStatus.Confirmed && _items.Count == 0)
            throw new DomainException("orders.no_items", "An order needs at least one item.");

        var previous = Status;
        Status = target;
        switch (target)
        {
            case OrderStatus.Confirmed: ConfirmedAt = at; break;
            case OrderStatus.Draft: ConfirmedAt = null; break;
            case OrderStatus.Shipped: ShippedAt = at; break;
            case OrderStatus.Delivered:
                DeliveredAt = at;
                if (PaymentMethod == PaymentMethod.CashOnDelivery && PaymentStatus == PaymentStatus.Unpaid)
                {
                    // With a delivery company the cash is with the courier until remitted; otherwise staff collected it.
                    if (DeliveryCompanyId is null)
                        SetPaymentStatus(PaymentStatus.Paid, userId, at, "Cash received on delivery by ÀLara Line staff.");
                    else
                        SetPaymentStatus(PaymentStatus.CollectedByCourier, userId, at, "Cash collected by the courier on delivery.");
                }
                break;
            case OrderStatus.Cancelled: CancelledAt = at; VoidUnpaid(userId, at); break;
            case OrderStatus.Returned: ReturnedAt = at; VoidUnpaid(userId, at); break;
        }
        _history.Add(new OrderHistoryEntry(Id, OrderEventType.Status, previous.ToString(), target.ToString(), notes, userId, at));
    }

    // ---- payment -------------------------------------------------------------------------

    /// <summary>Records a direct payment (non-COD methods).</summary>
    public void MarkPaid(Guid? userId, DateTimeOffset at, string? notes)
    {
        if (PaymentMethod == PaymentMethod.CashOnDelivery)
            throw new DomainException("orders.cod_paid_via_remittance",
                "Cash-on-delivery orders are marked paid when the courier remits the cash.");
        if (Status is OrderStatus.Cancelled or OrderStatus.Returned || PaymentStatus != PaymentStatus.Unpaid)
            throw new DomainException("orders.cannot_mark_paid", $"A {Status} order with payment {PaymentStatus} cannot be marked paid.");
        SetPaymentStatus(PaymentStatus.Paid, userId, at, notes);
    }

    /// <summary>The courier handed over the cash for this delivered COD order.</summary>
    public void MarkRemitted(Guid remittanceId, Guid? userId, DateTimeOffset at)
    {
        if (PaymentStatus != PaymentStatus.CollectedByCourier || CourierRemittanceId is not null)
            throw new DomainException("remittances.order_not_collectable",
                $"Order {OrderNumber} is not awaiting remittance (payment status {PaymentStatus}).");
        CourierRemittanceId = remittanceId;
        SetPaymentStatus(PaymentStatus.Paid, userId, at, "Cash remitted by the courier.");
    }

    private void VoidUnpaid(Guid? userId, DateTimeOffset at)
    {
        if (PaymentStatus is PaymentStatus.Unpaid or PaymentStatus.CollectedByCourier)
            SetPaymentStatus(PaymentStatus.Void, userId, at, null);
    }

    private void SetPaymentStatus(PaymentStatus status, Guid? userId, DateTimeOffset at, string? notes)
    {
        var previous = PaymentStatus;
        PaymentStatus = status;
        if (status == PaymentStatus.Paid) PaidAt = at;
        _history.Add(new OrderHistoryEntry(Id, OrderEventType.Payment, previous.ToString(), status.ToString(), notes, userId, at));
    }

    private void EnsureNotFinal(string action)
    {
        if (IsFinal)
            throw new DomainException("orders.final", $"You cannot {action} a {Status.ToString().ToLowerInvariant()} order.");
    }

    private static string Required(string value, string what) =>
        string.IsNullOrWhiteSpace(value) ? throw new DomainException("validation.required", $"{what} is required.") : value.Trim();

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    /// <summary>"+961 70-123 456" → "+96170123456", so phone searches match however the number was typed.</summary>
    public static string NormalizePhone(string phone)
    {
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        return phone.TrimStart().StartsWith('+') ? "+" + digits : digits;
    }
}

/// <summary>An order line. Name, type and price are snapshots taken when the line was added.</summary>
public sealed class OrderItem
{
    private readonly List<OrderItemComponent> _components = [];

    private OrderItem() { }

    public OrderItem(Guid orderId, Guid productId, string productName, ProductType productType,
        int quantity, decimal unitPrice, decimal lineDiscount,
        IReadOnlyList<(Guid ProductId, string Name, int QuantityPerBundle, decimal UnitPrice)> bundleComponents,
        bool isGift = false)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        ProductId = productId;
        ProductName = productName;
        ProductType = productType;
        IsGift = isGift;
        Quantity = Guard.Positive(quantity, $"Quantity of '{productName}'");
        UnitPrice = isGift ? 0 : Guard.Money(Guard.NotNegative(unitPrice, "Unit price"));
        LineDiscount = isGift ? 0 : Guard.Money(Guard.NotNegative(lineDiscount, $"Discount on '{productName}'"));
        var gross = Guard.Money(UnitPrice * Quantity);
        if (LineDiscount > gross)
            throw new DomainException("orders.line_discount_exceeds_total", $"The discount on '{productName}' exceeds the line total.");
        LineTotal = gross - LineDiscount;

        if (productType == ProductType.Bundle)
        {
            if (bundleComponents.Count == 0)
                throw new DomainException("bundles.empty", $"Bundle '{productName}' has no components.");
            // Split the bundle's line total across its components in proportion to their full prices,
            // so per-product profit reports can attribute bundle revenue. The last component takes the rounding remainder.
            var weights = bundleComponents.Select(c => c.UnitPrice * c.QuantityPerBundle).ToList();
            var totalWeight = weights.Sum();
            var allocated = 0m;
            for (var i = 0; i < bundleComponents.Count; i++)
            {
                var c = bundleComponents[i];
                var share = i == bundleComponents.Count - 1
                    ? LineTotal - allocated
                    : Guard.Money(totalWeight > 0
                        ? LineTotal * weights[i] / totalWeight
                        : LineTotal * c.QuantityPerBundle / bundleComponents.Sum(x => x.QuantityPerBundle));
                allocated += share;
                _components.Add(new OrderItemComponent(Id, c.ProductId, c.Name, c.QuantityPerBundle, c.QuantityPerBundle * Quantity, share));
            }
        }
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public ProductType ProductType { get; private set; }
    public bool IsGift { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal LineDiscount { get; private set; }
    public decimal LineTotal { get; private set; }
    public IReadOnlyList<OrderItemComponent> Components => _components;
}

/// <summary>Bundle composition at the time of the order, so later bundle edits never alter this order.</summary>
public sealed class OrderItemComponent
{
    private OrderItemComponent() { }

    internal OrderItemComponent(Guid orderItemId, Guid componentProductId, string componentName,
        int quantityPerBundle, int totalQuantity, decimal allocatedRevenue)
    {
        Id = Guid.CreateVersion7();
        AllocatedRevenue = allocatedRevenue;
        OrderItemId = orderItemId;
        ComponentProductId = componentProductId;
        ComponentName = componentName;
        QuantityPerBundle = quantityPerBundle;
        TotalQuantity = totalQuantity;
    }

    public Guid Id { get; private set; }
    public Guid OrderItemId { get; private set; }
    public Guid ComponentProductId { get; private set; }
    public string ComponentName { get; private set; } = "";
    public int QuantityPerBundle { get; private set; }
    public int TotalQuantity { get; private set; }
    /// <summary>This component's share of the bundle line total (for per-product revenue and profit).</summary>
    public decimal AllocatedRevenue { get; private set; }
}

/// <summary>Append-only audit of status and payment changes.</summary>
public sealed class OrderHistoryEntry
{
    private OrderHistoryEntry() { }

    internal OrderHistoryEntry(Guid orderId, OrderEventType eventType, string? oldValue, string newValue,
        string? notes, Guid? changedByUserId, DateTimeOffset changedAt)
    {
        Id = Guid.CreateVersion7();
        OrderId = orderId;
        EventType = eventType;
        OldValue = oldValue;
        NewValue = newValue;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        ChangedByUserId = changedByUserId;
        ChangedAt = changedAt;
    }

    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public OrderEventType EventType { get; private set; }
    public string? OldValue { get; private set; }
    public string NewValue { get; private set; } = "";
    public string? Notes { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
}
