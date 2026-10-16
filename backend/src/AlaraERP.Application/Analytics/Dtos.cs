using AlaraERP.Domain.Sales;

namespace AlaraERP.Application.Analytics;

public enum Grouping { Day, Week, Month }

/// <summary>Inclusive business-date range (Asia/Beirut). Defaults to the last 30 days.</summary>
public sealed record AnalyticsQuery(DateOnly? From, DateOnly? To, Grouping GroupBy = Grouping.Day);

/// <summary>
/// Definitions (also shown in the UI):
/// a sale counts on the day the order is confirmed (Confirmed/Shipped/Delivered; cancelled and returned orders are excluded).
/// NetSales = items − discounts (excl. delivery fee). CostOfGoods = recorded cost of the units shipped.
/// GrossProfit = NetSales − CostOfGoods. DeliveryResult = delivery fees charged − independently recorded delivery costs.
/// Profit = GrossProfit + DeliveryResult. CashReceived = paid orders by payment date (an order is paid when it is sold).
/// Cost and profit figures are null unless the caller has costs.view.
/// </summary>
public sealed record ProfitTotals(
    int Orders, int UnitsSold, decimal GrossSales, decimal Discounts, decimal NetSales, decimal AverageOrderValue,
    decimal DeliveryFees, decimal? DeliveryCosts, decimal? DeliveryResult,
    decimal? CostOfGoods, decimal? GrossProfit, decimal? GrossMarginPercent, decimal? Profit,
    decimal CashReceived, int UncostedUnits);

public sealed record PeriodPoint(
    DateOnly PeriodStart, DateOnly PeriodEnd, int Orders, int UnitsSold, decimal NetSales,
    decimal? CostOfGoods, decimal? GrossProfit, decimal? Profit, decimal CashReceived);

public sealed record ChannelPoint(OrderSource Source, int Orders, decimal NetSales, decimal? GrossProfit);

public sealed record AnalyticsSummaryDto(
    DateOnly From, DateOnly To, Grouping GroupBy, string Currency, bool IncludesCosts,
    ProfitTotals Totals, IReadOnlyList<PeriodPoint> Series, IReadOnlyList<ChannelPoint> ByChannel);

/// <summary>
/// Per individual product. Units and sales include units sold inside bundles (bundle revenue is split across
/// components by their full prices). AverageCost is today's on-hand cost; UnitMargin = price − AverageCost.
/// </summary>
public sealed record ProductProfitDto(
    Guid ProductId, string Name, string? CategoryName, bool IsActive,
    decimal SellingPrice, decimal? StandardCost, decimal? AverageCost, decimal? UnitMargin, decimal? UnitMarginPercent,
    int UnitsSold, int UnitsSoldInBundles, decimal NetSales, decimal? CostOfGoods, decimal? GrossProfit, decimal? GrossMarginPercent,
    int UncostedUnits);

public sealed record BundleProfitDto(
    Guid ProductId, string Name, bool IsActive, decimal SellingPrice, decimal? ComponentsCost, decimal? UnitMargin,
    decimal? UnitMarginPercent, int SetsSold, decimal NetSales, decimal? CostOfGoods, decimal? GrossProfit, decimal? GrossMarginPercent);

public sealed record ProductAnalyticsDto(
    DateOnly From, DateOnly To, string Currency, bool IncludesCosts,
    IReadOnlyList<ProductProfitDto> Products, IReadOnlyList<BundleProfitDto> Bundles);

public sealed record CustomerLoyaltyDto(
    Guid CustomerId, string Name, string Phone, string? City,
    int OrderCount, int RepeatOrders, decimal TotalOrdered, decimal TotalPaid, decimal AverageOrderValue,
    double? AverageDaysBetweenOrders, DateTimeOffset FirstOrderAt, DateTimeOffset LastOrderAt);

public sealed record CustomerAnalyticsDto(
    DateOnly From, DateOnly To, string Currency, IReadOnlyList<CustomerLoyaltyDto> Customers);
