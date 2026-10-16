using AlaraERP.Api.Security;
using AlaraERP.Application.Common;
using AlaraERP.Application.Dashboard;
using AlaraERP.Application.Inventory;
using AlaraERP.Application.Sales;
using AlaraERP.Application.Common.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

[Route("api/customers")]
public sealed class CustomersController(CustomerService customers) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Orders.View)]
    public Task<PagedResult<CustomerDto>> List([FromQuery] CustomerListQuery query, CancellationToken ct) => customers.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Orders.View)]
    public Task<CustomerDto> Get(Guid id, CancellationToken ct) => customers.GetAsync(id, ct);

    [HttpPost]
    [HasPermission(Orders.Create)]
    [ProducesResponseType<CustomerDto>(201)]
    public async Task<IActionResult> Create(CustomerRequest request, CancellationToken ct)
    {
        var customer = await customers.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = customer.Id }, customer);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Orders.Update)]
    public Task<CustomerDto> Update(Guid id, CustomerRequest request, CancellationToken ct) => customers.UpdateAsync(id, request, ct);
}

[Route("api/orders")]
public sealed class OrdersController(OrderService orders) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(Orders.View)]
    public Task<PagedResult<OrderListItemDto>> List([FromQuery] OrderListQuery query, CancellationToken ct) => orders.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Orders.View)]
    public Task<OrderDetailDto> Get(Guid id, CancellationToken ct) => orders.GetAsync(id, ct);

    /// <summary>Creates a draft, or with <c>confirm: true</c> confirms it and deducts stock immediately.</summary>
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

    /// <summary>Recipient details; allowed until the order is final.</summary>
    [HttpPut("{id:guid}/recipient")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> UpdateRecipient(Guid id, UpdateRecipientRequest request, CancellationToken ct) =>
        orders.UpdateRecipientAsync(id, request, ct);

    [HttpPost("{id:guid}/confirm")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Confirm(Guid id, TransitionRequest request, CancellationToken ct) => orders.ConfirmAsync(id, request, ct);
    /// <summary>Hold the stock until a time (from a sold order: undoes the sale and holds the stock again).</summary>
    [HttpPost("{id:guid}/reserve")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Reserve(Guid id, ReserveOrderRequest request, CancellationToken ct) => orders.ReserveAsync(id, request, ct);

    /// <summary>Change how long a reserved order is held.</summary>
    [HttpPut("{id:guid}/reservation")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> ChangeReservation(Guid id, ReserveOrderRequest request, CancellationToken ct) =>
        orders.ChangeReservationAsync(id, request, ct);

    /// <summary>Which reserved (unsold) orders are holding these products.</summary>
    [HttpGet("reserved-stock")]
    [HasPermission(Orders.View)]
    public async Task<ActionResult<IReadOnlyList<ReservedStockDto>>> ReservedStock(
        [FromQuery] Guid[] productIds, [FromQuery] Guid? excludeOrderId, CancellationToken ct)
    {
        if (productIds.Length is 0 or > 200)
            return ProblemWithCode(400, "validation_failed", "Pass between 1 and 200 productIds.");
        return Ok(await orders.ReservedStockAsync(productIds, excludeOrderId, ct));
    }

    [HttpPost("{id:guid}/ship")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Ship(Guid id, TransitionRequest request, CancellationToken ct) => orders.ShipAsync(id, request, ct);

    [HttpPost("{id:guid}/deliver")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Deliver(Guid id, TransitionRequest request, CancellationToken ct) => orders.DeliverAsync(id, request, ct);

    [HttpPost("{id:guid}/return")]
    [HasPermission(Orders.Update)]
    public Task<OrderDetailDto> Return(Guid id, TransitionRequest request, CancellationToken ct) => orders.ReturnAsync(id, request, ct);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(Orders.Cancel)]
    public Task<OrderDetailDto> Cancel(Guid id, TransitionRequest request, CancellationToken ct) => orders.CancelAsync(id, request, ct);

    /// <summary>Super Admin-only pre-launch cleanup. Restores held/deducted stock, then permanently removes the order.</summary>
    [HttpDelete("{id:guid}")]
    [HasPermission(Orders.Delete)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await orders.DeleteAsync(id, ct);
        return NoContent();
    }
}

[Route("api/delivery-companies")]
public sealed class DeliveryCompaniesController(DeliveryService delivery) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(DeliveryCompanies.Manage)]
    public Task<IReadOnlyList<DeliveryCompanyDto>> List([FromQuery] bool includeInactive, CancellationToken ct) =>
        delivery.ListCompaniesAsync(includeInactive, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(DeliveryCompanies.Manage)]
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
}

[Route("api/delivery-expenses")]
public sealed class DeliveryExpensesController(DeliveryService delivery) : ApiControllerBase
{
    [HttpGet]
    [HasPermission(DeliveryCompanies.Manage)]
    public Task<PagedResult<DeliveryExpenseDto>> List([FromQuery] DeliveryExpenseListQuery query, CancellationToken ct) =>
        delivery.ListExpensesAsync(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(DeliveryCompanies.Manage)]
    public Task<DeliveryExpenseDto> Get(Guid id, CancellationToken ct) => delivery.GetExpenseAsync(id, ct);

    [HttpPost]
    [HasPermission(DeliveryCompanies.Manage)]
    [ProducesResponseType<DeliveryExpenseDto>(201)]
    public async Task<IActionResult> Create(DeliveryExpenseRequest request, CancellationToken ct)
    {
        var expense = await delivery.CreateExpenseAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = expense.Id }, expense);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(DeliveryCompanies.Manage)]
    public Task<DeliveryExpenseDto> Update(Guid id, DeliveryExpenseRequest request, CancellationToken ct) =>
        delivery.UpdateExpenseAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(DeliveryCompanies.Manage)]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await delivery.DeleteExpenseAsync(id, ct);
        return NoContent();
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
public sealed class SettingsController(IOptions<ErpOptions> options, InventoryFeatureService features) : ApiControllerBase
{
    public sealed record ErpSettingsDto(string BaseCurrency, string TimeZone, decimal DefaultDeliveryFee, int ExpiryWarningDays,
        bool ExpiryDatesEnabled, IReadOnlyList<string> EnabledPaymentMethods);

    [HttpGet]
    public async Task<ErpSettingsDto> Get(CancellationToken ct)
    {
        var o = options.Value;
        // Only cash on delivery is offered until the business confirms other methods.
        return new ErpSettingsDto(o.BaseCurrency, o.TimeZone, o.DefaultDeliveryFee, o.ExpiryWarningDays,
            await features.ExpiryDatesEnabledAsync(ct), ["CashOnDelivery"]);
    }

    [HttpGet("features")]
    [Authorize(Roles = SystemRoles.SuperAdmin)]
    public Task<InventoryFeatureSettingsDto> GetFeatures(CancellationToken ct) => features.GetAsync(ct);

    [HttpPut("features/expiry-dates")]
    [Authorize(Roles = SystemRoles.SuperAdmin)]
    public Task<InventoryFeatureSettingsDto> SetExpiryDates(UpdateExpiryDatesRequest request, CancellationToken ct) =>
        features.SetExpiryDatesAsync(request.Enabled, ct);
}
