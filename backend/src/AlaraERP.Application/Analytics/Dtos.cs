using AlaraERP.Domain.Finance;
using AlaraERP.Domain.Sales;

namespace AlaraERP.Application.Analytics;

public enum Grouping { Day, Week, Month }

/// <summary>Inclusive business-date range (Asia/Beirut). Defaults to the last 30 days.</summary>
public sealed record AnalyticsQuery(DateOnly? From, DateOnly? To, Grouping GroupBy = Grouping.Day);

/// <summary>
/// Definitions (also shown in the UI):
/// a sale counts on the day the order is sold (cancelled and returned orders are excluded).
/// NetSales = items − discounts. CostOfGoods = cost of the units customers paid for (free gift lines excluded).
/// GrossProfit = NetSales − CostOfGoods. Gifts = cost of free gift lines on sold orders + stock taken out as a
/// sample or gift. Expenses = bills dated in the range (electricity, internet, models, wages…).
/// Profit (net profit) = GrossProfit − Gifts − Expenses. CashReceived = paid orders by payment date (paid when sold).
/// Cost, gift-cost, expense and profit figures are null unless the caller has costs.view.
/// </summary>
public sealed record ProfitTotals(
    int Orders, int UnitsSold, int GiftUnits, decimal GrossSales, decimal Discounts, decimal NetSales, decimal AverageOrderValue,
    decimal? CostOfGoods, decimal? GrossProfit, decimal? GrossMarginPercent,
    decimal? Gifts, decimal? Expenses, decimal? Profit, decimal? NetMarginPercent,
    decimal CashReceived, int UncostedUnits);

public sealed record PeriodPoint(
    DateOnly PeriodStart, DateOnly PeriodEnd, int Orders, int UnitsSold, decimal NetSales,
    decimal? CostOfGoods, decimal? GrossProfit, decimal? Gifts, decimal? Expenses, decimal? Profit, decimal CashReceived);

public enum GiftSource { Order, TakenOut }

/// <summary>
/// Something given away for free: gift lines on a sold order, or stock taken out as a sample or gift (e.g. a PR box).
/// Cost is null without costs.view, or when the units have no recorded cost.
/// </summary>
public sealed record GiftDto(
    DateTimeOffset At, GiftSource Source, Guid? OrderId, string? OrderNumber, string? Recipient,
    string Description, int Units, decimal? Cost);

public sealed record ChannelPoint(OrderSource Source, int Orders, decimal NetSales, decimal? GrossProfit);

public sealed record ExpenseCategoryPoint(ExpenseCategory Category, decimal Amount);

public sealed record AnalyticsSummaryDto(
    DateOnly From, DateOnly To, Grouping GroupBy, string Currency, bool IncludesCosts,
    ProfitTotals Totals, IReadOnlyList<PeriodPoint> Series, IReadOnlyList<ChannelPoint> ByChannel,
    /// <summary>Bills in the range by category; null without costs.view.</summary>
    IReadOnlyList<ExpenseCategoryPoint>? ExpensesByCategory = null,
    /// <summary>Everything given away in the range, newest first.</summary>
    IReadOnlyList<GiftDto>? Gifts = null);

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
