using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Analytics;

/// <summary>Sales, cost and profit over a date range. Read-only; may read every module (like the dashboard).</summary>
public sealed class AnalyticsService(IAlaraDbContext db, BusinessClock clock, ICurrentUser currentUser, IOptions<ErpOptions> options)
{
    private const int MaxDays = 3 * 366;
    private const int MaxPeriods = 400;
    private bool CanSeeCosts => currentUser.HasPermission(Permissions.Costs.View);

    public async Task<AnalyticsSummaryDto> SummaryAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = Range(query);
        var periods = Periods(from, to, query.GroupBy);
        var (start, end) = (clock.StartOfDayUtc(from), clock.StartOfDayUtc(to.AddDays(1)));
        var costs = CanSeeCosts;

        var orders = await SalesOrders(start, end)
            .Select(o => new
            {
                o.Id, o.ConfirmedAt, o.Source, o.Subtotal, o.DiscountTotal, o.ShippingTotal, o.CourierFee,
                GrossSales = o.Items.Sum(i => i.UnitPrice * i.Quantity),
                LineDiscounts = o.Items.Sum(i => i.LineDiscount),
            })
            .ToListAsync(ct);
        var costLines = await SaleCostsAsync(orders.Select(o => o.Id).ToList(), ct);
        var byOrder = costLines.GroupBy(m => m.OrderId).ToDictionary(g => g.Key, g => (
            Units: g.Sum(m => m.Quantity),
            Cost: g.Where(m => m.UnitCost != null).Sum(m => m.Quantity * m.UnitCost!.Value),
            Uncosted: g.Where(m => m.UnitCost == null).Sum(m => m.Quantity)));

        var paid = await db.Orders.AsNoTracking()
            .Where(o => o.PaymentStatus == PaymentStatus.Paid && o.PaidAt >= start && o.PaidAt < end)
            .Select(o => new { o.PaidAt, Net = o.GrandTotal - (o.CourierRemittanceId != null ? (o.CourierFee ?? 0) : 0) })
            .ToListAsync(ct);

        var rows = orders.Select(o =>
        {
            var c = byOrder.GetValueOrDefault(o.Id);
            var net = o.Subtotal - o.DiscountTotal;
            return new
            {
                Period = PeriodOf(clock.ToBusinessDate(o.ConfirmedAt!.Value), query.GroupBy),
                o.Source, o.GrossSales, Discounts = o.LineDiscounts + o.DiscountTotal, Net = net,
                o.ShippingTotal, Courier = o.CourierFee ?? 0,
                c.Units, c.Cost, c.Uncosted, Gross = net - c.Cost,
            };
        }).ToList();

        var series = periods.Select(p =>
        {
            var inPeriod = rows.Where(r => r.Period == p.Key).ToList();
            var gross = inPeriod.Sum(r => r.Gross);
            return new PeriodPoint(p.Start, p.End, inPeriod.Count, inPeriod.Sum(r => r.Units), Guard.Money(inPeriod.Sum(r => r.Net)),
                costs ? Guard.Money(inPeriod.Sum(r => r.Cost)) : null,
                costs ? Guard.Money(gross) : null,
                costs ? Guard.Money(gross + inPeriod.Sum(r => r.ShippingTotal - r.Courier)) : null,
                Guard.Money(paid.Where(x => PeriodOf(clock.ToBusinessDate(x.PaidAt!.Value), query.GroupBy) == p.Key).Sum(x => x.Net)));
        }).ToList();

        var netSales = rows.Sum(r => r.Net);
        var cogs = rows.Sum(r => r.Cost);
        var grossProfit = netSales - cogs;
        var deliveryResult = rows.Sum(r => r.ShippingTotal - r.Courier);
        var totals = new ProfitTotals(
            rows.Count, rows.Sum(r => r.Units), Guard.Money(rows.Sum(r => r.GrossSales)), Guard.Money(rows.Sum(r => r.Discounts)),
            Guard.Money(netSales), rows.Count == 0 ? 0 : Guard.Money(netSales / rows.Count),
            Guard.Money(rows.Sum(r => r.ShippingTotal)),
            costs ? Guard.Money(rows.Sum(r => r.Courier)) : null,
            costs ? Guard.Money(deliveryResult) : null,
            costs ? Guard.Money(cogs) : null,
            costs ? Guard.Money(grossProfit) : null,
            costs && netSales > 0 ? Math.Round(grossProfit / netSales * 100, 1) : null,
            costs ? Guard.Money(grossProfit + deliveryResult) : null,
            Guard.Money(paid.Sum(x => x.Net)),
            costs ? rows.Sum(r => r.Uncosted) : 0);

        var byChannel = Enum.GetValues<OrderSource>().Select(s =>
        {
            var r = rows.Where(x => x.Source == s).ToList();
            return new ChannelPoint(s, r.Count, Guard.Money(r.Sum(x => x.Net)), costs ? Guard.Money(r.Sum(x => x.Gross)) : null);
        }).ToList();

        return new AnalyticsSummaryDto(from, to, query.GroupBy, options.Value.BaseCurrency, costs, totals, series, byChannel);
    }

    public async Task<ProductAnalyticsDto> ProductsAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = Range(query);
        var (start, end) = (clock.StartOfDayUtc(from), clock.StartOfDayUtc(to.AddDays(1)));
        var costs = CanSeeCosts;

        var orders = await SalesOrders(start, end).Select(o => new { o.Id, o.Subtotal, o.DiscountTotal }).ToDictionaryAsync(o => o.Id, ct);
        // Order-level discounts are spread over lines in proportion to their totals.
        decimal Factor(Guid orderId) => orders[orderId].Subtotal > 0 ? (orders[orderId].Subtotal - orders[orderId].DiscountTotal) / orders[orderId].Subtotal : 0;

        var soldOrderIds = orders.Keys.ToList();
        var items = await db.OrderItems.AsNoTracking().Where(i => soldOrderIds.Contains(i.OrderId))
            .Select(i => new
            {
                i.OrderId, i.ProductId, i.ProductType, i.Quantity, i.LineTotal,
                Components = i.Components.Select(c => new { c.ComponentProductId, c.TotalQuantity, c.AllocatedRevenue }).ToList(),
            })
            .ToListAsync(ct);
        var costLines = await SaleCostsAsync(soldOrderIds, ct);
        var costByOrderProduct = costLines.GroupBy(m => (m.OrderId, m.ProductId)).ToDictionary(g => g.Key, g => (
            Cost: g.Where(m => m.UnitCost != null).Sum(m => m.Quantity * m.UnitCost!.Value),
            Costed: g.Where(m => m.UnitCost != null).Sum(m => m.Quantity),
            Uncosted: g.Where(m => m.UnitCost == null).Sum(m => m.Quantity)));

        var revenue = new Dictionary<Guid, decimal>();
        var direct = new Dictionary<Guid, int>();
        var inBundles = new Dictionary<Guid, int>();
        var bundleSales = new Dictionary<Guid, (int Sets, decimal Net, decimal Cost)>();
        void Add<T>(Dictionary<Guid, T> d, Guid k, T v, Func<T, T, T> plus) => d[k] = d.TryGetValue(k, out var cur) ? plus(cur, v) : v;

        foreach (var item in items)
        {
            var factor = Factor(item.OrderId);
            if (item.ProductType == ProductType.Individual)
            {
                Add(revenue, item.ProductId, item.LineTotal * factor, (a, b) => a + b);
                Add(direct, item.ProductId, item.Quantity, (a, b) => a + b);
                continue;
            }
            var bundleCost = 0m;
            foreach (var c in item.Components)
            {
                Add(revenue, c.ComponentProductId, c.AllocatedRevenue * factor, (a, b) => a + b);
                Add(inBundles, c.ComponentProductId, c.TotalQuantity, (a, b) => a + b);
                var oc = costByOrderProduct.GetValueOrDefault((item.OrderId, c.ComponentProductId));
                // Shipment rows are aggregated per order/product, because reservations deliberately do not retain
                // an order-line id. Spread the known cost over all units of that product so a partially uncosted
                // order cannot invent extra cost for the bundle rows.
                var shippedUnits = oc.Costed + oc.Uncosted;
                bundleCost += shippedUnits > 0 ? oc.Cost / shippedUnits * c.TotalQuantity : 0;
            }
            var soFar = bundleSales.GetValueOrDefault(item.ProductId);
            bundleSales[item.ProductId] = (soFar.Sets + item.Quantity, soFar.Net + item.LineTotal * factor, soFar.Cost + bundleCost);
        }

        var cogs = costByOrderProduct.GroupBy(kv => kv.Key.ProductId)
            .ToDictionary(g => g.Key, g => (Cost: g.Sum(x => x.Value.Cost), Uncosted: g.Sum(x => x.Value.Uncosted)));
        var averageCosts = await AverageOnHandCostsAsync(ct);

        var catalogue = await db.Products.AsNoTracking()
            .Select(p => new
            {
                p.Id, p.Name, p.Type, p.IsActive, p.SellingPrice, p.StandardCost,
                Category = db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                Components = p.BundleItems.Select(b => new { b.ComponentProductId, b.Quantity }).ToList(),
            })
            .ToListAsync(ct);
        decimal? CurrentCost(Guid id, decimal? standard) => averageCosts.TryGetValue(id, out var avg) ? avg : standard;
        decimal? Pct(decimal? part, decimal whole) => part is { } p && whole > 0 ? Math.Round(p / whole * 100, 1) : null;

        var products = catalogue.Where(p => p.Type == ProductType.Individual && (p.IsActive || revenue.ContainsKey(p.Id)))
            .Select(p =>
            {
                var net = Guard.Money(revenue.GetValueOrDefault(p.Id));
                var cost = cogs.GetValueOrDefault(p.Id);
                var avg = CurrentCost(p.Id, p.StandardCost);
                var unitMargin = avg is { } a ? p.SellingPrice - a : (decimal?)null;
                return new ProductProfitDto(p.Id, p.Name, p.Category, p.IsActive, p.SellingPrice,
                    costs ? p.StandardCost : null, costs ? Round4(avg) : null,
                    costs ? Round2(unitMargin) : null, costs ? Pct(unitMargin, p.SellingPrice) : null,
                    direct.GetValueOrDefault(p.Id) + inBundles.GetValueOrDefault(p.Id), inBundles.GetValueOrDefault(p.Id), net,
                    costs ? Guard.Money(cost.Cost) : null, costs ? Guard.Money(net - cost.Cost) : null,
                    costs ? Pct(net - cost.Cost, net) : null, costs ? cost.Uncosted : 0);
            })
            .OrderByDescending(p => p.NetSales).ThenBy(p => p.Name).ToList();

        var bundles = catalogue.Where(p => p.Type == ProductType.Bundle && (p.IsActive || bundleSales.ContainsKey(p.Id)))
            .Select(b =>
            {
                var componentCosts = b.Components.Select(c => (catalogue.FirstOrDefault(x => x.Id == c.ComponentProductId) is { } comp
                    ? CurrentCost(comp.Id, comp.StandardCost) : null) * c.Quantity).ToList();
                decimal? setCost = componentCosts.Count > 0 && componentCosts.All(c => c is not null) ? componentCosts.Sum() : null;
                var unitMargin = setCost is { } sc ? b.SellingPrice - sc : (decimal?)null;
                var sold = bundleSales.GetValueOrDefault(b.Id);
                return new BundleProfitDto(b.Id, b.Name, b.IsActive, b.SellingPrice,
                    costs ? Round2(setCost) : null, costs ? Round2(unitMargin) : null, costs ? Pct(unitMargin, b.SellingPrice) : null,
                    sold.Sets, Guard.Money(sold.Net),
                    costs ? Guard.Money(sold.Cost) : null, costs ? Guard.Money(sold.Net - sold.Cost) : null,
                    costs ? Pct(sold.Net - sold.Cost, sold.Net) : null);
            })
            .OrderByDescending(b => b.NetSales).ThenBy(b => b.Name).ToList();

        return new ProductAnalyticsDto(from, to, options.Value.BaseCurrency, costs, products, bundles);
    }

    public async Task<CustomerAnalyticsDto> CustomersAsync(AnalyticsQuery query, CancellationToken ct)
    {
        var (from, to) = Range(query);
        var (start, end) = (clock.StartOfDayUtc(from), clock.StartOfDayUtc(to.AddDays(1)));
        var orders = await SalesOrders(start, end).Where(o => o.CustomerId != null)
            .Select(o => new { CustomerId = o.CustomerId!.Value, o.ConfirmedAt, o.GrandTotal, o.PaymentStatus })
            .ToListAsync(ct);
        var customerIds = orders.Select(o => o.CustomerId).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => customerIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name, c.Phone, c.City }).ToDictionaryAsync(c => c.Id, ct);

        var rows = orders.GroupBy(o => o.CustomerId).Select(group =>
        {
            var customer = customers[group.Key];
            var dates = group.Select(o => o.ConfirmedAt!.Value).Order().ToList();
            var total = Guard.Money(group.Sum(o => o.GrandTotal));
            var count = dates.Count;
            var averageDays = count > 1 ? Math.Round((dates[^1] - dates[0]).TotalDays / (count - 1), 1) : (double?)null;
            return new CustomerLoyaltyDto(customer.Id, customer.Name, customer.Phone, customer.City,
                count, Math.Max(0, count - 1), total,
                Guard.Money(group.Where(o => o.PaymentStatus == PaymentStatus.Paid).Sum(o => o.GrandTotal)),
                count == 0 ? 0 : Guard.Money(total / count), averageDays, dates[0], dates[^1]);
        }).OrderByDescending(c => c.OrderCount).ThenByDescending(c => c.TotalOrdered).ThenBy(c => c.Name).ToList();

        return new CustomerAnalyticsDto(from, to, options.Value.BaseCurrency, rows);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>A confirmed order is a committed sale. Cancelled and returned orders are excluded.</summary>
    private IQueryable<Order> SalesOrders(DateTimeOffset start, DateTimeOffset end) =>
        db.Orders.AsNoTracking().Where(o => o.ConfirmedAt >= start && o.ConfirmedAt < end
                                            && (o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Shipped || o.Status == OrderStatus.Delivered));

    /// <summary>
    /// Confirmation records immutable shipment movements immediately; the movement rows hold the exact batch costs.
    /// </summary>
    private async Task<List<SaleCostLine>> SaleCostsAsync(IReadOnlyCollection<Guid> orderIds, CancellationToken ct)
    {
        if (orderIds.Count == 0) return [];

        var shipped = await db.StockMovements.AsNoTracking()
            // Net of returns: reopening a confirmed order puts its stock back (Return) and re-confirming takes it
            // again (Shipment), so only the net units — and their cost — belong to the sale.
            .Where(m => m.ReferenceType == MovementReference.Order
                        && (m.Type == MovementType.Shipment || m.Type == MovementType.Return)
                        && orderIds.Contains(m.ReferenceId))
            .Select(m => new SaleCostLine(m.ReferenceId, m.ProductId, -m.QuantityChange, m.UnitCost))
            .ToListAsync(ct);
        var shippedOrderIds = shipped.Select(m => m.OrderId).ToHashSet();
        var reserved = await db.StockReservations.AsNoTracking()
            .Where(r => orderIds.Contains(r.OrderId) && r.Status == ReservationStatus.Active)
            .Select(r => new { r.OrderId, r.ProductId, r.Quantity })
            .ToListAsync(ct);
        var averageCosts = reserved.Count > 0 ? await AverageOnHandCostsAsync(ct) : new Dictionary<Guid, decimal>();
        shipped.AddRange(reserved.Where(r => !shippedOrderIds.Contains(r.OrderId)).Select(r =>
            new SaleCostLine(r.OrderId, r.ProductId, r.Quantity,
                averageCosts.TryGetValue(r.ProductId, out var unitCost) ? unitCost : null)));
        return shipped;
    }

    private sealed record SaleCostLine(Guid OrderId, Guid ProductId, int Quantity, decimal? UnitCost);

    /// <summary>Weighted average cost of the units on hand today, falling back to the product's standard cost.</summary>
    private async Task<Dictionary<Guid, decimal>> AverageOnHandCostsAsync(CancellationToken ct)
    {
        var rows = await (from bb in db.BatchBalances.AsNoTracking()
                          join b in db.Batches.AsNoTracking() on bb.BatchId equals b.Id
                          join p in db.Products.AsNoTracking() on b.ProductId equals p.Id
                          where bb.QuantityOnHand > 0
                          select new { b.ProductId, bb.QuantityOnHand, BatchCost = b.UnitCost, p.StandardCost })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ProductId)
            .Where(g => g.All(r => r.BatchCost is not null || r.StandardCost is not null))
            .ToDictionary(g => g.Key, g =>
                g.Sum(r => r.QuantityOnHand * (r.BatchCost ?? r.StandardCost)!.Value) / g.Sum(r => r.QuantityOnHand));
    }

    private (DateOnly From, DateOnly To) Range(AnalyticsQuery q)
    {
        var to = q.To ?? clock.Today;
        if (to == DateOnly.MaxValue)
            throw new DomainException("analytics.invalid_range", "The end date is outside the supported reporting range.");
        var from = q.From ?? (to.DayNumber >= 29 ? to.AddDays(-29) : DateOnly.MinValue);
        if (from > to) throw new DomainException("analytics.invalid_range", "The start date must be on or before the end date.");
        if (to.DayNumber - from.DayNumber > MaxDays) throw new DomainException("analytics.range_too_long", "Choose a range of at most three years.");
        return (from, to);
    }

    /// <summary>
    /// Periods covering the range. Key = natural period start (matches <see cref="PeriodOf"/>);
    /// Start/End = the part of the period inside the range (for labels).
    /// </summary>
    private static List<(DateOnly Key, DateOnly Start, DateOnly End)> Periods(DateOnly from, DateOnly to, Grouping grouping)
    {
        var list = new List<(DateOnly, DateOnly, DateOnly)>();
        for (var key = PeriodOf(from, grouping); key <= to; key = Next(key, grouping))
        {
            list.Add((key, key < from ? from : key, Min(Next(key, grouping).AddDays(-1), to)));
            if (list.Count > MaxPeriods)
                throw new DomainException("analytics.too_many_periods", "That range has too many days to show one by one; group by week or month.");
        }
        return list;
    }

    /// <summary>Start of the day / ISO week (Monday) / month containing the date.</summary>
    private static DateOnly PeriodOf(DateOnly d, Grouping g) => g switch
    {
        Grouping.Week => d.AddDays(-(((int)d.DayOfWeek + 6) % 7)),
        Grouping.Month => new DateOnly(d.Year, d.Month, 1),
        _ => d,
    };

    private static DateOnly Next(DateOnly periodStart, Grouping g) => g switch
    {
        Grouping.Week => periodStart.AddDays(7),
        Grouping.Month => periodStart.AddMonths(1),
        _ => periodStart.AddDays(1),
    };

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;
    private static decimal? Round2(decimal? v) => v is { } x ? Math.Round(x, 2, MidpointRounding.AwayFromZero) : null;
    private static decimal? Round4(decimal? v) => v is { } x ? Math.Round(x, 4, MidpointRounding.AwayFromZero) : null;
}
