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

public sealed class OrderLineInputValidator : AbstractValidator<OrderLineInput>
{
    public OrderLineInputValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 10_000);
        RuleFor(x => x.LineDiscount).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true);
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
        v.RuleForEach(x => items(x)).SetValidator(new OrderLineInputValidator()).OverridePropertyName("items");
        v.RuleFor(x => items(x)).Must(i => i is null || i.Select(l => l.ProductId).Distinct().Count() == i.Count)
            .WithMessage("Each product can appear only once; change its quantity instead.").OverridePropertyName("items");
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
        OrderRules.Apply(this, x => x.Recipient, x => x.Items, x => x.DiscountTotal, x => x.Notes);
    }
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

public sealed class UpdateDeliveryRequestValidator : AbstractValidator<UpdateDeliveryRequest>
{
    public UpdateDeliveryRequestValidator()
    {
        RuleFor(x => x.Recipient).NotNull().SetValidator(new RecipientInputValidator());
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.CourierFee).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true).When(x => x.CourierFee.HasValue);
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}

public sealed class ShipOrderRequestValidator : AbstractValidator<ShipOrderRequest>
{
    public ShipOrderRequestValidator()
    {
        RuleFor(x => x.TrackingNumber).MaximumLength(100);
        RuleFor(x => x.CourierFee).GreaterThanOrEqualTo(0).PrecisionScale(12, 2, true).When(x => x.CourierFee.HasValue);
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

public sealed class CreateRemittanceRequestValidator : AbstractValidator<CreateRemittanceRequest>
{
    public CreateRemittanceRequestValidator()
    {
        RuleFor(x => x.DeliveryCompanyId).NotEmpty();
        RuleFor(x => x.OrderIds).NotEmpty().WithMessage("Select at least one order.")
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count).WithMessage("An order is selected twice.");
        RuleFor(x => x.Notes).MaximumLength(2000);
    }
}
