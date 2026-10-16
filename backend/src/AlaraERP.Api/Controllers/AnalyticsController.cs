using AlaraERP.Api.Security;
using AlaraERP.Application.Analytics;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

/// <summary>Sales, cost and profit. Cost and profit fields are null unless the caller has costs.view.</summary>
[Route("api/analytics")]
public sealed class AnalyticsController(AnalyticsService analytics) : ApiControllerBase
{
    [HttpGet("summary")]
    [HasPermission(Reports.View)]
    public Task<AnalyticsSummaryDto> Summary([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.SummaryAsync(query, ct);

    [HttpGet("products")]
    [HasPermission(Reports.View)]
    public Task<ProductAnalyticsDto> Products([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.ProductsAsync(query, ct);

    [HttpGet("customers")]
    [HasPermission(Reports.View)]
    public Task<CustomerAnalyticsDto> Customers([FromQuery] AnalyticsQuery query, CancellationToken ct) => analytics.CustomersAsync(query, ct);
}
