using AlaraERP.Domain.Finance;
using FluentValidation;

namespace AlaraERP.Application.Finance;

public sealed class ExpenseRequestValidator : AbstractValidator<ExpenseRequest>
{
    public ExpenseRequestValidator()
    {
        RuleFor(x => x.Category).IsInEnum();
        RuleFor(x => x.IncurredOn).NotEmpty()
            .Must(d => d.Year >= 2000).WithMessage("Choose a valid date.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Enter an amount greater than zero.").PrecisionScale(12, 2, true);
        RuleFor(x => x.Description).MaximumLength(Expense.DescriptionMaxLength);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
