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
        [OrderStatus.Draft] = [OrderStatus.Reserved, OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Reserved] = [OrderStatus.Confirmed, OrderStatus.Cancelled],
        [OrderStatus.Confirmed] = [OrderStatus.Reserved, OrderStatus.Cancelled, OrderStatus.Returned],
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

    public decimal Subtotal { get; private set; }
    public decimal DiscountTotal { get; private set; }
    public decimal GrandTotal { get; private set; }
    public string CurrencyCode { get; private set; } = "";

    public string? Notes { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? ReservedAt { get; private set; }
    /// <summary>When the hold on the stock should be reviewed. Nothing is released automatically; an alert is raised.</summary>
    public DateTimeOffset? ReservedUntil { get; private set; }
    /// <summary>Set once the "reservation time is up" alert has been sent, so it is sent only once per deadline.</summary>
    public DateTimeOffset? ReservationExpiryNotifiedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<OrderItem> Items => _items;
    public IReadOnlyList<OrderHistoryEntry> History => _history;

    public bool IsFinal => Status is OrderStatus.Cancelled or OrderStatus.Returned;

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

    /// <summary>Lines and discount can change until an order is cancelled or returned.</summary>
    public void ReplaceLines(IReadOnlyList<OrderItem> items, decimal discountTotal,
        Guid warehouseId, OrderSource source, PaymentMethod paymentMethod)
    {
        if (Status is not (OrderStatus.Draft or OrderStatus.Reserved or OrderStatus.Confirmed))
            throw new DomainException("orders.not_editable", "Cancelled and returned orders can't be edited.");
        if (items.Count == 0)
            throw new DomainException("orders.no_items", "An order needs at least one item.");

        _items.Clear();
        _items.AddRange(items);
        WarehouseId = warehouseId;
        Source = source;
        PaymentMethod = paymentMethod;

        Subtotal = Guard.Money(_items.Sum(i => i.LineTotal));
        DiscountTotal = Guard.Money(Guard.NotNegative(discountTotal, "Order discount"));
        if (DiscountTotal > Subtotal)
            throw new DomainException("orders.discount_exceeds_subtotal", "The order discount cannot exceed the items subtotal.");
        GrandTotal = Guard.Money(Subtotal - DiscountTotal);
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

    /// <summary>Holds the stock for the customer until <paramref name="until"/> (from a new, legacy-draft or sold order).</summary>
    public void Reserve(DateTimeOffset until, Guid? userId, DateTimeOffset at, string? notes = null)
    {
        EnsureFutureDeadline(until, at);
        ApplyTransition(OrderStatus.Reserved, userId, at, notes);
        ReservedAt = at;
        SetDeadline(until, userId, at, null);
    }

    /// <summary>Gives the customer more (or less) time; a new deadline gets its own alert.</summary>
    public void ChangeReservationDeadline(DateTimeOffset until, Guid? userId, DateTimeOffset at, string? notes = null)
    {
        if (Status != OrderStatus.Reserved)
            throw new DomainException("orders.not_reserved", "Only reserved orders have a reservation time.");
        EnsureFutureDeadline(until, at);
        SetDeadline(until, userId, at, notes);
    }

    /// <summary>The reservation-time-is-up alert went out for the current deadline.</summary>
    public void MarkReservationExpiryNotified(DateTimeOffset at) => ReservationExpiryNotifiedAt = at;

    public bool ReservationExpiredAt(DateTimeOffset now) => Status == OrderStatus.Reserved && ReservedUntil is { } u && u <= now;

    private void SetDeadline(DateTimeOffset until, Guid? userId, DateTimeOffset at, string? notes)
    {
        var previous = ReservedUntil;
        ReservedUntil = until;
        ReservationExpiryNotifiedAt = null;
        _history.Add(new OrderHistoryEntry(Id, OrderEventType.Reservation, previous?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mmZ"),
            until.ToUniversalTime().ToString("yyyy-MM-ddTHH:mmZ"), notes, userId, at));
    }

    private static void EnsureFutureDeadline(DateTimeOffset until, DateTimeOffset now)
    {
        if (until <= now)
            throw new DomainException("orders.reservation_in_past", "Choose a reservation time in the future.");
        if (until > now.AddDays(90))
            throw new DomainException("orders.reservation_too_long", "A reservation can last at most 90 days.");
    }

    public void TransitionTo(OrderStatus target, Guid? userId, DateTimeOffset at, string? notes = null)
    {
        if (target == OrderStatus.Reserved)
            throw new InvalidOperationException("Use Reserve(...) so the reservation time is recorded.");
        ApplyTransition(target, userId, at, notes);
    }

    private void ApplyTransition(OrderStatus target, Guid? userId, DateTimeOffset at, string? notes)
    {
        if (!CanTransition(Status, target))
            throw new DomainException("orders.invalid_transition", $"An order cannot go from {Status} to {target}.");
        if (target == OrderStatus.Confirmed && _items.Count == 0)
            throw new DomainException("orders.no_items", "An order needs at least one item.");

        var previous = Status;
        Status = target;
        switch (target)
        {
            case OrderStatus.Confirmed: ConfirmedAt = at; break;
            case OrderStatus.Reserved: ConfirmedAt = null; break;
            case OrderStatus.Cancelled: CancelledAt = at; break;
            case OrderStatus.Returned: ReturnedAt = at; break;
        }
        _history.Add(new OrderHistoryEntry(Id, OrderEventType.Status, previous.ToString(), target.ToString(), notes, userId, at));

        // Selling is the sale: the order is paid at once. Undoing the sale
        // makes it unpaid again; a cancelled or returned order's money is taken back out (Void).
        switch (target)
        {
            case OrderStatus.Confirmed: SetPaymentStatus(PaymentStatus.Paid, userId, at, null); break;
            case OrderStatus.Reserved when PaymentStatus == PaymentStatus.Paid: SetPaymentStatus(PaymentStatus.Unpaid, userId, at, null); break;
            case OrderStatus.Cancelled or OrderStatus.Returned: SetPaymentStatus(PaymentStatus.Void, userId, at, null); break;
        }
    }

    // ---- payment -------------------------------------------------------------------------

    private void SetPaymentStatus(PaymentStatus status, Guid? userId, DateTimeOffset at, string? notes)
    {
        if (status == PaymentStatus) return;
        var previous = PaymentStatus;
        PaymentStatus = status;
        PaidAt = status == PaymentStatus.Paid ? at : null;
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
