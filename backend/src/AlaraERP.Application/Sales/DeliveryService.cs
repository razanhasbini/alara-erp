using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Sales;

/// <summary>Delivery companies and expenses, deliberately independent from customer orders.</summary>
public sealed class DeliveryService(IAlaraDbContext db, BusinessClock clock, ICurrentUser currentUser, IUserDirectory users)
{
    public async Task<IReadOnlyList<DeliveryCompanyDto>> ListCompaniesAsync(bool includeInactive, CancellationToken ct) =>
        await Project(db.DeliveryCompanies.AsNoTracking().Where(d => includeInactive || d.IsActive)
                .OrderByDescending(d => d.IsActive).ThenBy(d => d.Name))
            .ToListAsync(ct);

    public async Task<DeliveryCompanyDto> GetCompanyAsync(Guid id, CancellationToken ct) =>
        await Project(db.DeliveryCompanies.AsNoTracking().Where(d => d.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Delivery company", id);

    public async Task<DeliveryCompanyDto> CreateCompanyAsync(DeliveryCompanyRequest request, CancellationToken ct)
    {
        await EnsureNameFreeAsync(request.Name, null, ct);
        var company = new DeliveryCompany(request.Name, request.Phone);
        company.Update(request.Name, request.Phone, request.IsActive);
        db.DeliveryCompanies.Add(company);
        await db.SaveChangesAsync(ct);
        return await GetCompanyAsync(company.Id, ct);
    }

    public async Task<DeliveryCompanyDto> UpdateCompanyAsync(Guid id, DeliveryCompanyRequest request, CancellationToken ct)
    {
        var company = await db.DeliveryCompanies.FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw new NotFoundException("Delivery company", id);
        await EnsureNameFreeAsync(request.Name, id, ct);
        company.Update(request.Name, request.Phone, request.IsActive);
        await db.SaveChangesAsync(ct);
        return await GetCompanyAsync(id, ct);
    }

    public async Task<PagedResult<DeliveryExpenseDto>> ListExpensesAsync(DeliveryExpenseListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var expenses = db.DeliveryExpenses.AsNoTracking();
        if (query.DeliveryCompanyId is { } companyId) expenses = expenses.Where(e => e.DeliveryCompanyId == companyId);
        if (query.From is { } from) expenses = expenses.Where(e => e.IncurredOn >= from);
        if (query.To is { } to) expenses = expenses.Where(e => e.IncurredOn <= to);
        var total = await expenses.CountAsync(ct);
        var rows = await expenses.OrderByDescending(e => e.IncurredOn).ThenByDescending(e => e.CreatedAt)
            .Skip(page.Skip).Take(page.SafePageSize)
            .Select(e => new
            {
                Expense = e,
                CompanyName = db.DeliveryCompanies.Where(c => c.Id == e.DeliveryCompanyId).Select(c => c.Name).First(),
            })
            .ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(rows.Select(r => r.Expense.RecordedByUserId), ct);
        return new PagedResult<DeliveryExpenseDto>(rows.Select(r => ToDto(r.Expense, r.CompanyName, names)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<DeliveryExpenseDto> GetExpenseAsync(Guid id, CancellationToken ct)
    {
        var expense = await db.DeliveryExpenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
                      ?? throw new NotFoundException("Delivery expense", id);
        var company = await db.DeliveryCompanies.Where(c => c.Id == expense.DeliveryCompanyId).Select(c => c.Name).SingleAsync(ct);
        var names = await users.GetDisplayNamesAsync([expense.RecordedByUserId], ct);
        return ToDto(expense, company, names);
    }

    public async Task<DeliveryExpenseDto> CreateExpenseAsync(DeliveryExpenseRequest request, CancellationToken ct)
    {
        await RequireCompanyAsync(request.DeliveryCompanyId, active: true, ct);
        if (request.IncurredOn > clock.Today)
            throw new DomainException("delivery_expenses.future_date", "Delivery cost date cannot be in the future.");
        var expense = new DeliveryExpense(request.DeliveryCompanyId, request.IncurredOn, request.Amount,
            request.DeliveryCount, request.Notes, currentUser.UserId);
        db.DeliveryExpenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return await GetExpenseAsync(expense.Id, ct);
    }

    public async Task<DeliveryExpenseDto> UpdateExpenseAsync(Guid id, DeliveryExpenseRequest request, CancellationToken ct)
    {
        var expense = await db.DeliveryExpenses.FirstOrDefaultAsync(e => e.Id == id, ct)
                      ?? throw new NotFoundException("Delivery expense", id);
        await RequireCompanyAsync(request.DeliveryCompanyId, active: false, ct);
        if (request.IncurredOn > clock.Today)
            throw new DomainException("delivery_expenses.future_date", "Delivery cost date cannot be in the future.");
        expense.ChangeCompany(request.DeliveryCompanyId);
        expense.Update(request.IncurredOn, request.Amount, request.DeliveryCount, request.Notes);
        await db.SaveChangesAsync(ct);
        return await GetExpenseAsync(expense.Id, ct);
    }

    public async Task DeleteExpenseAsync(Guid id, CancellationToken ct)
    {
        var expense = await db.DeliveryExpenses.FirstOrDefaultAsync(e => e.Id == id, ct)
                      ?? throw new NotFoundException("Delivery expense", id);
        db.DeliveryExpenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    private async Task EnsureNameFreeAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var normalized = name.Trim().ToLower();
        if (await db.DeliveryCompanies.AnyAsync(d => d.Name.ToLower() == normalized && d.Id != excludeId, ct))
            throw new ConflictException("delivery_companies.duplicate_name", $"A delivery company named '{name.Trim()}' already exists.");
    }

    private async Task<DeliveryCompany> RequireCompanyAsync(Guid id, bool active, CancellationToken ct)
    {
        var company = await db.DeliveryCompanies.FirstOrDefaultAsync(c => c.Id == id, ct)
                      ?? throw new NotFoundException("Delivery company", id);
        if (active && !company.IsActive)
            throw new DomainException("delivery_companies.inactive", $"{company.Name} is inactive.");
        return company;
    }

    private static DeliveryExpenseDto ToDto(DeliveryExpense e, string company,
        IReadOnlyDictionary<Guid, string> names) =>
        new(e.Id, e.DeliveryCompanyId, company, e.IncurredOn, e.Amount, e.DeliveryCount, e.Notes,
            e.RecordedByUserId is { } userId ? names.GetValueOrDefault(userId) : null, e.CreatedAt, e.UpdatedAt);

    private IQueryable<DeliveryCompanyDto> Project(IQueryable<DeliveryCompany> q) => q.Select(d => new DeliveryCompanyDto(
        d.Id, d.Name, d.Phone, d.IsActive,
        db.DeliveryExpenses.Where(e => e.DeliveryCompanyId == d.Id).Sum(e => (int?)e.DeliveryCount) ?? 0,
        db.DeliveryExpenses.Where(e => e.DeliveryCompanyId == d.Id).Sum(e => (decimal?)e.Amount) ?? 0));
}
