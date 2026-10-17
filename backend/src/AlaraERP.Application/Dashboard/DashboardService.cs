using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Inventory;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Dashboard;

public sealed record CatalogSummary(int ActiveProducts, int ActiveBundles, int InactiveProducts);

public sealed record OrderSummary(
    int Total, int Drafts, int Sold, int Reserved, int Cancelled, int Returned, int CreatedToday, int SoldToday);

/// <summary>Money figures, each with a precise definition (shown in the UI as the metric's tooltip).</summary>
public sealed record FinanceSummary(
    string Currency,
    /// <summary>Σ grand total of paid orders. An order is paid the moment it is sold.</summary>
    decimal CollectedRevenue,
    /// <summary>Same as above, restricted to orders paid this calendar month (business time zone).</summary>
    decimal CollectedThisMonth,
    /// <summary>Σ grand total of reserved orders: sales waiting for the customer.</summary>
    decimal ReservedValue,
    /// <summary>Σ bills dated this calendar month. Null unless the caller has costs.view.</summary>
    decimal? BillsThisMonth);

public sealed record DailyPoint(DateOnly Date, int Orders, decimal Value);

public sealed record SourcePoint(OrderSource Source, int Orders, decimal Value);

public sealed record WarehouseUnits(Guid WarehouseId, string Code, string Name, int OnHand, int Reserved);

public sealed record LowStockItem(Guid ProductId, string Name, int Available, int Threshold);

public sealed record ExpiryItem(Guid ProductId, string Name, string BatchNumber, DateOnly ExpiryDate, string WarehouseCode, int Units);

public sealed record InventorySummary(
    int UnitsOnHand, int UnitsReserved, int LowStockCount, int OutOfStockCount, int ExpiredUnits, int ExpiringUnits, int ExpiryWarningDays,
    IReadOnlyList<WarehouseUnits> ByWarehouse, IReadOnlyList<LowStockItem> LowStock, IReadOnlyList<ExpiryItem> Expiring);

public sealed record RecentOrder(Guid Id, string OrderNumber, OrderStatus Status, PaymentStatus PaymentStatus, string RecipientName,
    decimal GrandTotal, DateTimeOffset CreatedAt);

public sealed record DashboardDto(
    DateTimeOffset GeneratedAt,
    CatalogSummary? Catalog,
    OrderSummary? Orders,
    FinanceSummary? Finance,
    /// <summary>Last 30 days, by order creation date: orders that were not drafts, cancelled or returned.</summary>
    IReadOnlyList<DailyPoint>? DailyOrders,
    IReadOnlyList<SourcePoint>? BySource,
    IReadOnlyList<RecentOrder>? RecentOrders,
    InventorySummary? Inventory,
    IReadOnlyList<MovementDto>? RecentMovements);

/// <summary>Live aggregates. Each section is included only if the caller may see that module.</summary>
public sealed class DashboardService(
    IAlaraDbContext db, ICurrentUser currentUser, BusinessClock clock, StockQueryService stockQueries,
    IOptions<ErpOptions> options, InventoryFeatureService features)
{
    private static readonly OrderStatus[] CountedStatuses = [OrderStatus.Confirmed];

    public async Task<DashboardDto> GetAsync(CancellationToken ct)
    {
        var canProducts = currentUser.HasPermission(Permissions.Products.View);
        var canOrders = currentUser.HasPermission(Permissions.Orders.View);
        var canReports = currentUser.HasPermission(Permissions.Reports.View);
        var canInventory = currentUser.HasPermission(Permissions.Inventory.View);

        return new DashboardDto(
            clock.UtcNow,
            canProducts ? await CatalogAsync(ct) : null,
            canOrders || canReports ? await OrdersAsync(ct) : null,
            canReports ? await FinanceAsync(ct) : null,
            canReports ? await DailyAsync(ct) : null,
            canReports ? await BySourceAsync(ct) : null,
            canOrders ? await RecentOrdersAsync(ct) : null,
            canInventory ? await InventoryAsync(ct) : null,
            canInventory ? (await stockQueries.ListMovementsAsync(new MovementQuery(null, null, null, null, null, null, 1, 8), ct)).Items : null);
    }

    private async Task<CatalogSummary> CatalogAsync(CancellationToken ct)
    {
        var counts = await db.Products.GroupBy(p => new { p.Type, p.IsActive })
            .Select(g => new { g.Key.Type, g.Key.IsActive, Count = g.Count() }).ToListAsync(ct);
        return new CatalogSummary(
            counts.Where(c => c.IsActive && c.Type == ProductType.Individual).Sum(c => c.Count),
            counts.Where(c => c.IsActive && c.Type == ProductType.Bundle).Sum(c => c.Count),
            counts.Where(c => !c.IsActive).Sum(c => c.Count));
    }

    private async Task<OrderSummary> OrdersAsync(CancellationToken ct)
    {
        var byStatus = await db.Orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var startOfToday = clock.StartOfDayUtc(clock.Today);
        int Get(OrderStatus s) => byStatus.GetValueOrDefault(s);
        return new OrderSummary(byStatus.Values.Sum(), Get(OrderStatus.Draft), Get(OrderStatus.Confirmed), Get(OrderStatus.Reserved),
            Get(OrderStatus.Cancelled), Get(OrderStatus.Returned),
            await db.Orders.CountAsync(o => o.CreatedAt >= startOfToday, ct),
            await db.Orders.CountAsync(o => o.Status == OrderStatus.Confirmed && o.ConfirmedAt >= startOfToday, ct));
    }

    private async Task<FinanceSummary> FinanceAsync(CancellationToken ct)
    {
        var today = clock.Today;
        var monthStart = clock.StartOfDayUtc(new DateOnly(today.Year, today.Month, 1));
        var paid = db.Orders.Where(o => o.PaymentStatus == PaymentStatus.Paid);
        return new FinanceSummary(
            options.Value.BaseCurrency,
            await paid.SumAsync(o => o.GrandTotal, ct),
            await paid.Where(o => o.PaidAt >= monthStart).SumAsync(o => o.GrandTotal, ct),
            await db.Orders.Where(o => o.Status == OrderStatus.Reserved).SumAsync(o => o.GrandTotal, ct),
            currentUser.HasPermission(Permissions.Costs.View)
                ? await db.Expenses.Where(e => e.IncurredOn >= new DateOnly(today.Year, today.Month, 1)
                                               && e.IncurredOn <= new DateOnly(today.Year, today.Month, 1).AddMonths(1).AddDays(-1))
                    .SumAsync(e => e.Amount, ct)
                : null);
    }

    private async Task<IReadOnlyList<DailyPoint>> DailyAsync(CancellationToken ct)
    {
        var firstDay = clock.Today.AddDays(-29);
        var since = clock.StartOfDayUtc(firstDay);
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CreatedAt >= since && CountedStatuses.Contains(o.Status))
            .Select(o => new { o.CreatedAt, o.GrandTotal }).ToListAsync(ct);
        var byDay = orders.GroupBy(o => clock.ToBusinessDate(o.CreatedAt)).ToDictionary(g => g.Key, g => (g.Count(), g.Sum(x => x.GrandTotal)));
        return Enumerable.Range(0, 30).Select(i => firstDay.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var v) ? new DailyPoint(d, v.Item1, v.Item2) : new DailyPoint(d, 0, 0))
            .ToList();
    }

    private async Task<IReadOnlyList<SourcePoint>> BySourceAsync(CancellationToken ct)
    {
        var since = clock.StartOfDayUtc(clock.Today.AddDays(-29));
        var rows = await db.Orders.Where(o => o.CreatedAt >= since && CountedStatuses.Contains(o.Status))
            .GroupBy(o => o.Source).Select(g => new { g.Key, Count = g.Count(), Value = g.Sum(o => o.GrandTotal) })
            .ToListAsync(ct);
        return Enum.GetValues<OrderSource>()
            .Select(s => rows.FirstOrDefault(r => r.Key == s) is { } r ? new SourcePoint(s, r.Count, r.Value) : new SourcePoint(s, 0, 0))
            .ToList();
    }

    private async Task<IReadOnlyList<RecentOrder>> RecentOrdersAsync(CancellationToken ct) =>
        await db.Orders.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(8)
            .Select(o => new RecentOrder(o.Id, o.OrderNumber, o.Status, o.PaymentStatus, o.RecipientName, o.GrandTotal, o.CreatedAt))
            .ToListAsync(ct);

    private async Task<InventorySummary> InventoryAsync(CancellationToken ct)
    {
        var warningDays = options.Value.ExpiryWarningDays;
        var today = clock.Today;
        var warnUntil = today.AddDays(warningDays);
        var expiryEnabled = await features.ExpiryDatesEnabledAsync(ct);

        var byWarehouse = await (from w in db.Warehouses.AsNoTracking()
                                 where w.IsActive
                                 orderby w.Code
                                 select new WarehouseUnits(w.Id, w.Code, w.Name,
                                     db.StockBalances.Where(b => b.WarehouseId == w.Id).Sum(b => (int?)b.QuantityOnHand) ?? 0,
                                     db.StockBalances.Where(b => b.WarehouseId == w.Id).Sum(b => (int?)b.QuantityReserved) ?? 0))
            .ToListAsync(ct);

        var low = await stockQueries.ListStockAsync(new StockListQuery(null, null, null, StockFilter.Low, Sort: "available", PageSize: 8), ct);
        var outOfStock = await stockQueries.ListStockAsync(new StockListQuery(null, null, null, StockFilter.Out, PageSize: 1), ct);

        var batchUnits = db.BatchBalances.AsNoTracking().Where(bb => expiryEnabled && bb.QuantityOnHand > 0 && bb.Batch!.ExpiryDate != null);
        var expired = expiryEnabled ? await batchUnits.Where(bb => bb.Batch!.ExpiryDate < today).SumAsync(bb => (int?)bb.QuantityOnHand, ct) ?? 0 : 0;
        var expiring = expiryEnabled ? await batchUnits.Where(bb => bb.Batch!.ExpiryDate >= today && bb.Batch.ExpiryDate <= warnUntil)
            .SumAsync(bb => (int?)bb.QuantityOnHand, ct) ?? 0 : 0;
        var expiringList = expiryEnabled ? await (from bb in batchUnits
                                  join p in db.Products on bb.Batch!.ProductId equals p.Id
                                  join w in db.Warehouses on bb.WarehouseId equals w.Id
                                  where bb.Batch!.ExpiryDate <= warnUntil
                                  orderby bb.Batch!.ExpiryDate
                                  select new ExpiryItem(p.Id, p.Name, bb.Batch!.BatchNumber, bb.Batch.ExpiryDate!.Value, w.Code, bb.QuantityOnHand))
            .Take(8).ToListAsync(ct) : [];

        return new InventorySummary(
            byWarehouse.Sum(w => w.OnHand), byWarehouse.Sum(w => w.Reserved), low.TotalCount, outOfStock.TotalCount,
            expired, expiring, warningDays, byWarehouse,
            low.Items.Select(i => new LowStockItem(i.ProductId, i.Name, i.Available, i.LowStockThreshold)).ToList(),
            expiringList);
    }
}
