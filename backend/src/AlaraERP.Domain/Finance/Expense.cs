using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Finance;

/// <summary>What a bill is for. Models (photo shoots, content) are occasional; the others are usually monthly.</summary>
public enum ExpenseCategory { Electricity, Internet, Models, Wages, Other }

/// <summary>
/// A running cost of the business (a bill, wages, a model's fee). Counted against profit on the date it applies to,
/// independently of orders.
/// </summary>
public sealed class Expense : IAuditable
{
    public const int DescriptionMaxLength = 200;

    private Expense() { }

    public Expense(ExpenseCategory category, DateOnly incurredOn, decimal amount, string? description, string? notes,
        Guid? recordedByUserId)
    {
        Id = Guid.CreateVersion7();
        RecordedByUserId = recordedByUserId;
        Update(category, incurredOn, amount, description, notes);
    }

    public Guid Id { get; private set; }
    public ExpenseCategory Category { get; private set; }
    /// <summary>The date the cost applies to (for a monthly bill, any day in that month).</summary>
    public DateOnly IncurredOn { get; private set; }
    public decimal Amount { get; private set; }
    /// <summary>Short label, e.g. "Diana — October" or "Photo shoot, autumn collection".</summary>
    public string? Description { get; private set; }
    public string? Notes { get; private set; }
    public Guid? RecordedByUserId { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Update(ExpenseCategory category, DateOnly incurredOn, decimal amount, string? description, string? notes)
    {
        if (!Enum.IsDefined(category))
            throw new DomainException("expenses.invalid_category", "Choose what the bill is for.");
        if (amount <= 0)
            throw new DomainException("expenses.amount_required", "The amount must be greater than zero.");
        var label = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (label?.Length > DescriptionMaxLength)
            throw new DomainException("expenses.description_too_long", $"Keep the description under {DescriptionMaxLength} characters.");

        Category = category;
        IncurredOn = incurredOn;
        Amount = Guard.Money(amount);
        Description = label;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
