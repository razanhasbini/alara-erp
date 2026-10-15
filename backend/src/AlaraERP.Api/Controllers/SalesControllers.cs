using AlaraERP.Api.Security;
using AlaraERP.Application.Common;
using AlaraERP.Application.Dashboard;
using AlaraERP.Application.Sales;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

[Route("api/orders")]
public sealed class OrdersController(OrderService orders) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Orders.View)]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] OrderListQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Orders.View)]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>Creates a draft, or with <c>confirm: true</c> a confirmed order with stock reserved.</summary>
    [HttpPost]
    [HasPermission(Orders.Create)]
    [ProducesResponseType<OrderDetailDto>(201)]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken ct)
    {
        var order = await orders.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
    }

    /// <summary>Replaces lines and details of a draft order.</summary>
    [HttpPut("{id:guid}")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Update(Guid id, UpdateOrderRequest request, CancellationToken ct) => orders.UpdateAsync(id, request, ct);

    /// <summary>Recipient and courier details; allowed until the order is final.</summary>
    [HttpPut("{id:guid}/delivery")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> UpdateDelivery(Guid id, UpdateDeliveryRequest request, CancellationToken ct) =>
        orders.UpdateDeliveryAsync(id, request, ct);

    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Confirm(Guid id, TransitionRequest request, CancellationToken ct) => orders.ConfirmAsync(id, request, ct);

    [HttpPost("{id:guid}/reopen")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Reopen(Guid id, TransitionRequest request, CancellationToken ct) => orders.ReopenAsync(id, request, ct);

    [HttpPost("{id:guid}/ship")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Ship(Guid id, ShipOrderRequest request, CancellationToken ct) => orders.ShipAsync(id, request, ct);

    [HttpPost("{id:guid}/deliver")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Deliver(Guid id, TransitionRequest request, CancellationToken ct) => orders.DeliverAsync(id, request, ct);

    [HttpPost("{id:guid}/return")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Return(Guid id, TransitionRequest request, CancellationToken ct) => orders.ReturnAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Orders.Cancel)]
    public Task<OrderDetailDto> Cancel(Guid id, TransitionRequest request, CancellationToken ct) => orders.CancelAsync(id, request, ct);

    [HttpPost("{id:guid}/mark-paid")]
    [HasPermission(Orders.ManagePayments)]
    public Task<OrderDetailDto> MarkPaid(Guid id, TransitionRequest request, CancellationToken ct) => orders.MarkPaidAsync(id, request, ct);
}

[Route("api/delivery-companies")]
public sealed class DeliveryCompaniesController(DeliveryService delivery) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Orders.View)]
    public Task<IReadOnlyList<DeliveryCompanyDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        delivery.ListCompaniesAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Orders.View)]
    public Task<DeliveryCompanyDto> Get(Guid id, CancellationToken ct) => delivery.GetCompanyAsync(id, ct);

    [HttpPost]
    [HasPermission(DeliveryCompanies.Manage)]
    [ProducesResponseType<DeliveryCompanyDto>(201)]
    public async Task<IActionResult> Create(DeliveryCompanyRequest request, CancellationToken ct)
    {
        var company = await delivery.CreateCompanyAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = company.Id }, company);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(DeliveryCompanies.Manage)]
    public Task<DeliveryCompanyDto> Update(Guid id, DeliveryCompanyRequest request, CancellationToken ct) =>
        delivery.UpdateCompanyAsync(id, request, ct);

    [HttpGet("{id:guid}/awaiting-remittance")]
    [HasPermission(Orders.ManagePayments)]
    public Task<IReadOnlyList<RemittanceOrderDto>> AwaitingRemittance(Guid id, CancellationToken ct) =>
        delivery.AwaitingRemittanceAsync(id, ct);
}

[Route("api/courier-remittances")]
public sealed class CourierRemittancesController(DeliveryService delivery) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Orders.ManagePayments)]
    public Task<PagedResult<RemittanceDto>> List([FromQuery] Guid? deliveryCompanyId, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        delivery.ListRemittancesAsync(deliveryCompanyId, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Orders.ManagePayments)]
    public Task<RemittanceDto> Get(Guid id, CancellationToken ct) => delivery.GetRemittanceAsync(id, ct);

    [HttpPost]
    [HasPermission(Orders.ManagePayments)]
    [ProducesResponseType<RemittanceDto>(201)]
    public async Task<IActionResult> Create(CreateRemittanceRequest request, CancellationToken ct)
    {
        var remittance = await delivery.RecordRemittanceAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = remittance.Id }, remittance);
    }
}

[Route("api/dashboard")]
public sealed class DashboardController(DashboardService dashboard) : ApiControllerBase
{
    /// <summary>Sections are included according to the caller's permissions.</summary>
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(ct);
}

[Route("api/settings")]
public sealed class SettingsController(IOptions<ErpOptions> options) : ApiControllerBase
{
    public sealed record ErpSettingsDto(string BaseCurrency, string TimeZone, decimal DefaultDeliveryFee, int ExpiryWarningDays,
        IReadOnlyList<string> EnabledPaymentMethods);

    [HttpGet]
    public ErpSettingsDto Get()
    {
        var o = options.Value;
        // Only cash on delivery is offered until the business confirms other methods.
        return new ErpSettingsDto(o.BaseCurrency, o.TimeZone, o.DefaultDeliveryFee, o.ExpiryWarningDays, ["CashOnDelivery"]);
    }
}
