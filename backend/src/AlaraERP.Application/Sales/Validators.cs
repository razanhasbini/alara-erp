using FluentValidation;

namespace AlaraERP.Application.Sales;

public sealed class RecipientInputValidator : AbstractValidator<RecipientInput>
{
    public RecipientInputValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(40).Matches(@"^\+?[0-9 ()\-/]{6,40}$")
            .WithMessage("Enter a valid phone number.");
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(1000);
    }
}

public sealed class CustomerRequestValidator : AbstractValidator<CustomerRequest>
{
    public CustomerRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).NotEmpty().MaximumLength(40).Matches(@"^\+?[0-9 ()\-/]{6,40}$")
            .WithMessage("Enter a valid phone number.");
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => !string.IsNullOrWhiteSpace(x.Email));
        RuleFor(x => x.City).MaximumLength(100);
        RuleFor(x => x.Address).MaximumLength(1000);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class OrderLineInputValidator : AbstractValidator<OrderLineInput>
{
    public OrderLineInputValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10_000);
        RuleFor(x => x.LineDiscount).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true);
        RuleFor(x => x.LineDiscount).Equal(0).When(x => x.IsGift).WithMessage("A gift cannot have a discount because it is already free.");
    }
}

internal static class OrderRules
{
    public static void Apply<T>(AbstractValidator<T> v,
        Func<T, RecipientInput> recipient, Func<T, IReadOnlyList<OrderLineInput>> items,
        Func<T, decimal> discount, Func<T, string?> notes)
    {
        v.RuleFor(x => recipient(x)).NotNull().SetValidator(new RecipientInputValidator()).OverridePropertyName("recipient");
        v.RuleFor(x => items(x)).NotEmpty().WithMessage("Add at least one product.").OverridePropertyName("items");
        v.RuleFor(x => items(x)).Must(i => i is not null && i.Any(line => !line.IsGift))
            .WithMessage("Add at least one product to sell. A gift must be sent with an order.").OverridePropertyName("items");
        v.RuleForEach(x => items(x)).SetValidator(new OrderLineInputValidator()).OverridePropertyName("items");
        v.RuleFor(x => items(x)).Must(i => i is null || i.Select(l => (l.ProductId, l.IsGift)).Distinct().Count() == i.Count)
            .WithMessage("Each product can appear once as a sale and once as a gift; change its quantity instead.").OverridePropertyName("items");
        v.RuleFor(x => discount(x)).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true).OverridePropertyName("discountTotal");
        v.RuleFor(x => notes(x)).MaximumLength(2000).OverridePropertyName("notes");
    }
}

public sealed class CreateOrderRequestValidator : AbstractValidator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.ShippingTotal).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true).When(x => x.ShippingTotal.HasValue);
        RuleFor(x => x).Must(x => x.Confirm != x.ReserveUntil.HasValue).OverridePropertyName("reserveUntil")
            .WithMessage("Choose either “Sell now” or “Reserve until” a date and time.");
        OrderRules.Apply(this, x => x.Recipient, x => x.Items, x => x.DiscountTotal, x => x.Notes);
    }
}

public sealed class ReserveOrderRequestValidator : AbstractValidator<ReserveOrderRequest>
{
    public ReserveOrderRequestValidator() => RuleFor(x => x.Notes).MaximumLength(2000);
}

public sealed class UpdateOrderRequestValidator : AbstractValidator<UpdateOrderRequest>
{
    public UpdateOrderRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Source).IsInEnum();
        RuleFor(x => x.PaymentMethod).IsInEnum();
        RuleFor(x => x.ShippingTotal).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true);
        OrderRules.Apply(this, x => x.Recipient, x => x.Items, x => x.DiscountTotal, x => x.Notes);
    }
}

public sealed class UpdateRecipientRequestValidator : AbstractValidator<UpdateRecipientRequest>
{
    public UpdateRecipientRequestValidator()
    {
        RuleFor(x => x.Recipient).NotNull().SetValidator(new RecipientInputValidator());
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class DeliveryExpenseRequestValidator : AbstractValidator<DeliveryExpenseRequest>
{
    public DeliveryExpenseRequestValidator()
    {
        RuleFor(x => x.DeliveryCompanyId).NotEmpty();
        RuleFor(x => x.IncurredOn).NotEmpty();
        RuleFor(x => x.Amount).GreaterThan(0).PrecisionScale(12, 2, true);
        RuleFor(x => x.DeliveryCount).InclusiveBetween(1, 100_000);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class DeliveryCompanyRequestValidator : AbstractValidator<DeliveryCompanyRequest>
{
    public DeliveryCompanyRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Phone).MaximumLength(40);
    }
}
