using AlaraERP.Api.Security;
using AlaraERP.Application.Finance;
using Microsoft.AspNetCore.Mvc;
using static AlaraERP.Application.Common.Security.Permissions;

namespace AlaraERP.Api.Controllers;

/// <summary>Bills and running costs (admin only). They are subtracted from profit in analytics.</summary>
[Route("api/expenses")]
[HasPermission(Expenses.Manage)]
public sealed class ExpensesController(ExpenseService expenses) : ApiControllerBase
{
    [HttpGet]
    public Task<ExpenseListDto> List([FromQuery] ExpenseQuery query, CancellationToken ct) => expenses.ListAsync(query, ct);

    [HttpGet("{id:guid}")]
    public Task<ExpenseDto> Get(Guid id, CancellationToken ct) => expenses.GetAsync(id, ct);

    [HttpPost]
    [ProducesResponseType<ExpenseDto>(201)]
    public async Task<IActionResult> Create(ExpenseRequest request, CancellationToken ct)
    {
        var expense = await expenses.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { id = expense.Id }, expense);
    }

    [HttpPut("{id:guid}")]
    public Task<ExpenseDto> Update(Guid id, ExpenseRequest request, CancellationToken ct) => expenses.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await expenses.DeleteAsync(id, ct);
        return NoContent();
    }
}
