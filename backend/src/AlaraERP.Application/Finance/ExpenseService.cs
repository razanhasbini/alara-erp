using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Finance;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Finance;

/// <summary>Bills and running costs. They reduce net profit in analytics on the date they apply to.</summary>
public sealed class ExpenseService(IAlaraDbContext db, BusinessClock clock, ICurrentUser currentUser, IUserDirectory users)
{
    /// <summary>How far ahead a bill may be dated (e.g. wages entered before month end).</summary>
    private const int MaxDaysAhead = 62;

    public async Task<ExpenseListDto> ListAsync(ExpenseQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var inRange = db.Expenses.AsNoTracking();
        if (query.From is { } from) inRange = inRange.Where(e => e.IncurredOn >= from);
        if (query.To is { } to) inRange = inRange.Where(e => e.IncurredOn <= to);

        var byCategory = await inRange.GroupBy(e => e.Category)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(e => e.Amount) })
            .ToListAsync(ct);
        var listed = query.Category is { } category ? inRange.Where(e => e.Category == category) : inRange;
        var totalCount = await listed.CountAsync(ct);
        var rows = await listed.OrderByDescending(e => e.IncurredOn).ThenByDescending(e => e.CreatedAt)
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(rows.Select(r => r.RecordedByUserId), ct);

        return new ExpenseListDto(query.From, query.To,
            byCategory.Sum(c => c.Amount),
            Enum.GetValues<ExpenseCategory>().Select(c => byCategory.FirstOrDefault(x => x.Key == c) is { } x
                ? new ExpenseCategoryTotalDto(c, x.Count, x.Amount) : new ExpenseCategoryTotalDto(c, 0, 0)).ToList(),
            rows.Select(r => ToDto(r, names)).ToList(), page.SafePage, page.SafePageSize, totalCount);
    }

    public async Task<ExpenseDto> GetAsync(Guid id, CancellationToken ct)
    {
        var expense = await db.Expenses.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, ct)
                      ?? throw new NotFoundException("Bill", id);
        return ToDto(expense, await users.GetDisplayNamesAsync([expense.RecordedByUserId], ct));
    }

    public async Task<ExpenseDto> CreateAsync(ExpenseRequest request, CancellationToken ct)
    {
        EnsureDate(request.IncurredOn);
        var expense = new Expense(request.Category, request.IncurredOn, request.Amount, request.Description, request.Notes,
            currentUser.UserId);
        db.Expenses.Add(expense);
        await db.SaveChangesAsync(ct);
        return await GetAsync(expense.Id, ct);
    }

    public async Task<ExpenseDto> UpdateAsync(Guid id, ExpenseRequest request, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Bill", id);
        EnsureDate(request.IncurredOn);
        expense.Update(request.Category, request.IncurredOn, request.Amount, request.Description, request.Notes);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == id, ct) ?? throw new NotFoundException("Bill", id);
        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(ct);
    }

    private void EnsureDate(DateOnly date)
    {
        if (date > clock.Today.AddDays(MaxDaysAhead))
            throw new DomainException("expenses.date_too_far", "A bill can be dated at most two months ahead.");
    }

    private static ExpenseDto ToDto(Expense e, IReadOnlyDictionary<Guid, string> names) => new(
        e.Id, e.Category, e.IncurredOn, e.Amount, e.Description, e.Notes,
        e.RecordedByUserId is { } u ? names.GetValueOrDefault(u) : null, e.CreatedAt, e.UpdatedAt);
}
