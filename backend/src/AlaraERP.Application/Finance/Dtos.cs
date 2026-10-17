using AlaraERP.Domain.Finance;

namespace AlaraERP.Application.Finance;

public sealed record ExpenseRequest(ExpenseCategory Category, DateOnly IncurredOn, decimal Amount, string? Description, string? Notes);

/// <summary>Inclusive date range; both optional. Category narrows the list (totals by category always cover the range).</summary>
public sealed record ExpenseQuery(DateOnly? From, DateOnly? To, ExpenseCategory? Category, int Page = 1, int PageSize = 50);

public sealed record ExpenseDto(
    Guid Id, ExpenseCategory Category, DateOnly IncurredOn, decimal Amount, string? Description, string? Notes,
    string? RecordedBy, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ExpenseCategoryTotalDto(ExpenseCategory Category, int Count, decimal Amount);

public sealed record ExpenseListDto(
    DateOnly? From, DateOnly? To,
    /// <summary>All bills in the range, whatever the category filter.</summary>
    decimal Total, IReadOnlyList<ExpenseCategoryTotalDto> ByCategory,
    IReadOnlyList<ExpenseDto> Items, int Page, int PageSize, int TotalCount);
