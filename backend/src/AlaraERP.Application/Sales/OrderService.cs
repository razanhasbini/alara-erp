using AlaraERP.Application.Common;
using AlaraERP.Application.Inventory.Contracts;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Sales;

/// <summary>
/// Order entry and lifecycle. Every command runs in one transaction: the order change, the stock change
/// (through <see cref="IStockReservationService"/>) and the audit history commit together or not at all.
/// Concurrent edits of the same order are rejected by the order's xmin concurrency token.
/// </summary>
public sealed class OrderService(
    IAlaraDbContext db,
    IStockReservationService stock,
    IWarehouseDirectory warehouses,
    IDocumentNumbers numbers,
    BusinessClock clock,
    ICurrentUser currentUser,
    IUserDirectory users,
    IOptions<ErpOptions> options)
{
    private Guid? UserId => currentUser.UserId;

    // ---- queries -----------------------------------------------------------------------------

    public async Task<PagedResult<OrderListItemDto>> ListAsync(OrderListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = db.Orders.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Text.ContainsPattern(query.Search);
            var digits = new string(query.Search.Where(char.IsDigit).ToArray());
            q = q.Where(o => EF.Functions.Like(o.OrderNumber.ToLower(), term, "\\")
                          || EF.Functions.Like(o.RecipientName.ToLower(), term, "\\")
                          || (digits.Length >= 3 && o.RecipientPhone.Contains(digits))
                          || (o.TrackingNumber != null && EF.Functions.Like(o.TrackingNumber.ToLower(), term, "\\")));
        }
        if (query.Status is { } status) q = q.Where(o => o.Status == status);
        if (query.Statuses is { Length: > 0 } statuses) q = q.Where(o => statuses.Contains(o.Status));
        if (query.PaymentStatus is { } payment) q = q.Where(o => o.PaymentStatus == payment);
        if (query.Source is { } source) q = q.Where(o => o.Source == source);
        if (query.DeliveryCompanyId is { } company) q = q.Where(o => o.DeliveryCompanyId == company);
        if (query.CustomerId is { } customer) q = q.Where(o => o.CustomerId == customer);
        if (query.From is { } from) { var start = clock.StartOfDayUtc(from); q = q.Where(o => o.CreatedAt >= start); }
        if (query.To is { } to) { var end = clock.StartOfDayUtc(to.AddDays(1)); q = q.Where(o => o.CreatedAt < end); }

        IOrderedQueryable<Order> ordered = query.Sort switch
        {
            "created" => q.OrderBy(o => o.CreatedAt),
            "total" => q.OrderBy(o => o.GrandTotal),
            "-total" => q.OrderByDescending(o => o.GrandTotal),
            "number" => q.OrderBy(o => o.OrderNumber),
            _ => q.OrderByDescending(o => o.CreatedAt),
        };

        var total = await q.CountAsync(ct);
        var rows = await ordered.ThenByDescending(o => o.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(o => new
            {
                o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.PaymentMethod, o.Source,
                o.RecipientName, o.RecipientPhone, o.RecipientCity,
                ItemCount = o.Items.Sum(i => i.Quantity), o.GrandTotal, o.CurrencyCode,
                Company = db.DeliveryCompanies.Where(d => d.Id == o.DeliveryCompanyId).Select(d => d.Name).FirstOrDefault(),
                o.TrackingNumber, o.CreatedAt, o.CreatedByUserId,
            })
            .ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(rows.Select(r => r.CreatedByUserId), ct);

        return new PagedResult<OrderListItemDto>(rows.Select(r => new OrderListItemDto(
                r.Id, r.OrderNumber, r.Status, r.PaymentStatus, r.PaymentMethod, r.Source, r.RecipientName, r.RecipientPhone,
                r.RecipientCity, r.ItemCount, r.GrandTotal, r.CurrencyCode, r.Company, r.TrackingNumber, r.CreatedAt,
                r.CreatedByUserId is { } u ? names.GetValueOrDefault(u) : null)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<OrderDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var o = await db.Orders.AsNoTracking()
                    .Include(x => x.Items).ThenInclude(i => i.Components)
                    .Include(x => x.History)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Order", id);

        var warehouse = await warehouses.FindAsync(o.WarehouseId, ct);
        var company = o.DeliveryCompanyId is { } dc
            ? await db.DeliveryCompanies.Where(d => d.Id == dc).Select(d => d.Name).FirstOrDefaultAsync(ct)
            : null;
        var remittance = o.CourierRemittanceId is { } rid
            ? await db.CourierRemittances.Where(r => r.Id == rid).Select(r => r.RemittanceNumber).FirstOrDefaultAsync(ct)
            : null;
        var names = await users.GetDisplayNamesAsync(o.History.Select(h => h.ChangedByUserId).Append(o.CreatedByUserId), ct);
        string? Name(Guid? userId) => userId is { } u ? names.GetValueOrDefault(u) : null;

        var allowed = Enum.GetValues<OrderStatus>().Where(s => Order.CanTransition(o.Status, s)).ToList();
        return new OrderDetailDto(
            o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.PaymentMethod, o.Source, o.WarehouseId, warehouse?.Name ?? "?", o.CustomerId,
            o.RecipientName, o.RecipientPhone, o.RecipientCity, o.RecipientAddress,
            o.DeliveryCompanyId, company, o.TrackingNumber,
            o.Subtotal, o.DiscountTotal, o.ShippingTotal, o.CourierFee, o.GrandTotal, o.CurrencyCode,
            remittance, o.Notes, o.CreatedByUserId, Name(o.CreatedByUserId), o.CreatedAt, o.UpdatedAt,
            o.ConfirmedAt, o.ShippedAt, o.DeliveredAt, o.CancelledAt, o.ReturnedAt, o.PaidAt,
            allowed,
            o.Items.OrderBy(i => i.ProductName).Select(i => new OrderItemDto(
                i.Id, i.ProductId, i.ProductName, i.ProductType, i.IsGift, i.Quantity, i.UnitPrice, i.LineDiscount, i.LineTotal,
                i.Components.OrderBy(c => c.ComponentName)
                    .Select(c => new OrderItemComponentDto(c.ComponentProductId, c.ComponentName, c.QuantityPerBundle, c.TotalQuantity))
                    .ToList())).ToList(),
            o.History.OrderBy(h => h.ChangedAt).ThenBy(h => h.Id)
                .Select(h => new OrderHistoryDto(h.EventType, h.OldValue, h.NewValue, h.Notes, h.ChangedAt, Name(h.ChangedByUserId)))
                .ToList());
    }

    // ---- commands ----------------------------------------------------------------------------

    public async Task<OrderDetailDto> CreateAsync(CreateOrderRequest request, CancellationToken ct)
    {
        await EnsureWarehouseUsableAsync(request.WarehouseId, ct);
        await EnsureDeliveryCompanyUsableAsync(request.DeliveryCompanyId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var customer = await ResolveCustomerAsync(request.CustomerId, request.Recipient, ct);
        var number = await numbers.NextOrderNumberAsync(ct);
        var order = new Order(number, request.Source, request.WarehouseId, ToRecipient(request.Recipient),
            request.PaymentMethod, options.Value.BaseCurrency, UserId, clock.UtcNow);
        order.SetCustomer(customer.Id);
        order.ReplaceLines(await BuildItemsAsync(order.Id, request.Items, ct),
            request.DiscountTotal,
            request.ShippingTotal ?? options.Value.DefaultDeliveryFee,
            request.WarehouseId, request.Source, request.PaymentMethod);
        order.SetDelivery(request.DeliveryCompanyId, null, null);
        order.SetNotes(request.Notes);
        db.Orders.Add(order);

        if (request.Confirm)
        {
            order.TransitionTo(OrderStatus.Confirmed, UserId, clock.UtcNow);
            await stock.ReserveAsync(order.Id, order.WarehouseId, order.StockRequirements(), ct);
            // Make the staged reservation queryable inside this transaction, then deduct it now.
            // No reserved quantity is visible outside the confirmation transaction.
            await db.SaveChangesAsync(ct);
            await stock.ShipAsync(order.Id, order.OrderNumber, ct);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(order.Id, ct);
    }

    public Task<OrderDetailDto> UpdateAsync(Guid id, UpdateOrderRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            await EnsureWarehouseUsableAsync(request.WarehouseId, ct);
            await EnsureDeliveryCompanyUsableAsync(request.DeliveryCompanyId, ct);
            order.ReplaceLines(await BuildItemsAsync(order.Id, request.Items, ct),
                request.DiscountTotal, request.ShippingTotal,
                request.WarehouseId, request.Source, request.PaymentMethod);
            order.SetRecipient(ToRecipient(request.Recipient));
            order.SetCustomer((await ResolveCustomerAsync(request.CustomerId, request.Recipient, ct)).Id);
            order.SetDelivery(request.DeliveryCompanyId, order.TrackingNumber, order.CourierFee);
            order.SetNotes(request.Notes);
        }, ct);

    public Task<OrderDetailDto> UpdateDeliveryAsync(Guid id, UpdateDeliveryRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            await EnsureDeliveryCompanyUsableAsync(request.DeliveryCompanyId, ct);
            order.SetRecipient(ToRecipient(request.Recipient));
            order.SetDelivery(request.DeliveryCompanyId, request.TrackingNumber, request.CourierFee);
            order.SetNotes(request.Notes);
        }, ct);

    public Task<OrderDetailDto> ConfirmAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            await EnsureWarehouseUsableAsync(order.WarehouseId, ct);
            order.TransitionTo(OrderStatus.Confirmed, UserId, clock.UtcNow, request.Notes);
            await stock.ReserveAsync(order.Id, order.WarehouseId, order.StockRequirements(), ct);
            await db.SaveChangesAsync(ct);
            await stock.ShipAsync(order.Id, order.OrderNumber, ct);
        }, ct);

    public Task<OrderDetailDto> ReopenAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            order.TransitionTo(OrderStatus.Draft, UserId, clock.UtcNow, request.Notes);
            await stock.ReleaseAsync(order.Id, ct);
            await stock.ReturnShipmentAsync(order.Id, order.OrderNumber, ct);
        }, ct);

    public Task<OrderDetailDto> ShipAsync(Guid id, ShipOrderRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            var companyId = request.DeliveryCompanyId ?? order.DeliveryCompanyId;
            await EnsureDeliveryCompanyUsableAsync(companyId, ct);
            order.SetDelivery(companyId, request.TrackingNumber ?? order.TrackingNumber, request.CourierFee ?? order.CourierFee);
            order.TransitionTo(OrderStatus.Shipped, UserId, clock.UtcNow, request.Notes);
            // Current orders were deducted at confirmation. This only fulfils a reservation left
            // by an order confirmed before that workflow changed.
            await stock.ShipAsync(order.Id, order.OrderNumber, ct);
        }, ct);

    public Task<OrderDetailDto> DeliverAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, order =>
        {
            order.TransitionTo(OrderStatus.Delivered, UserId, clock.UtcNow, request.Notes);
            return Task.CompletedTask;
        }, ct);

    public Task<OrderDetailDto> ReturnAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            order.TransitionTo(OrderStatus.Returned, UserId, clock.UtcNow, request.Notes);
            await stock.ReturnShipmentAsync(order.Id, order.OrderNumber, ct);
        }, ct);

    public Task<OrderDetailDto> CancelAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, async order =>
        {
            var hadReservation = order.Status == OrderStatus.Confirmed;
            order.TransitionTo(OrderStatus.Cancelled, UserId, clock.UtcNow, request.Notes);
            if (hadReservation)
            {
                await stock.ReleaseAsync(order.Id, ct);
                await stock.ReturnShipmentAsync(order.Id, order.OrderNumber, ct);
            }
        }, ct);

    public Task<OrderDetailDto> MarkPaidAsync(Guid id, TransitionRequest request, CancellationToken ct) =>
        ChangeAsync(id, order =>
        {
            order.MarkPaid(UserId, clock.UtcNow, request.Notes);
            return Task.CompletedTask;
        }, ct);

    // ---- helpers -----------------------------------------------------------------------------

    private async Task<OrderDetailDto> ChangeAsync(Guid id, Func<Order, Task> change, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var order = await db.Orders.Include(o => o.Items).ThenInclude(i => i.Components)
                        .FirstOrDefaultAsync(o => o.Id == id, ct)
                    ?? throw new NotFoundException("Order", id);
        await change(order);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Snapshots each product's current name, price and (for bundles) composition.</summary>
    private async Task<List<OrderItem>> BuildItemsAsync(Guid orderId, IReadOnlyList<OrderLineInput> lines, CancellationToken ct)
    {
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking()
            .Include(p => p.BundleItems).ThenInclude(b => b.Component)
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, ct);

        return lines.Select(line =>
        {
            var product = products.GetValueOrDefault(line.ProductId)
                          ?? throw new DomainException("orders.unknown_product", $"Product '{line.ProductId}' does not exist.");
            if (!product.IsActive)
                throw new DomainException("orders.inactive_product", $"'{product.Name}' is inactive and cannot be sold.");
            var components = product.BundleItems
                .Select(b => (b.ComponentProductId, b.Component!.Name, b.Quantity, b.Component.SellingPrice))
                .ToList();
            if (product.Type == ProductType.Bundle && product.BundleItems.Any(b => !b.Component!.IsActive))
                throw new DomainException("orders.inactive_component", $"Bundle '{product.Name}' contains an inactive product.");
            return new OrderItem(orderId, product.Id, product.Name, product.Type, line.Quantity,
                product.SellingPrice, line.LineDiscount, components, line.IsGift);
        }).ToList();
    }

    private async Task EnsureWarehouseUsableAsync(Guid warehouseId, CancellationToken ct)
    {
        var warehouse = await warehouses.FindAsync(warehouseId, ct) ?? throw new NotFoundException("Warehouse", warehouseId);
        if (!warehouse.IsActive)
            throw new DomainException("warehouses.inactive", $"Warehouse {warehouse.Code} is inactive.");
    }

    private async Task EnsureDeliveryCompanyUsableAsync(Guid? companyId, CancellationToken ct)
    {
        if (companyId is not { } id) return;
        var company = await db.DeliveryCompanies.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id, ct)
                      ?? throw new NotFoundException("Delivery company", id);
        if (!company.IsActive)
            throw new DomainException("delivery_companies.inactive", $"{company.Name} is inactive.");
    }

    /// <summary>
    /// Orders always link to a durable customer. Selecting a customer keeps that identity even if the delivery
    /// recipient is edited; typing a new phone reuses an existing profile or creates one automatically.
    /// </summary>
    private async Task<Customer> ResolveCustomerAsync(Guid? customerId, RecipientInput recipient, CancellationToken ct)
    {
        if (customerId is { } id)
            return await db.Customers.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Customer", id);

        var phone = Customer.NormalizePhoneKey(recipient.Phone);
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.NormalizedPhone == phone, ct);
        if (existing is not null) return existing;

        var customer = new Customer(recipient.Name, recipient.Phone, null, recipient.City, recipient.Address, null);
        db.Customers.Add(customer);
        return customer;
    }

    private static OrderRecipient ToRecipient(RecipientInput r) => new(r.Name, r.Phone, r.City, r.Address);
}
