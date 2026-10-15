using AlaraERP.Domain.Inventory;
using FluentValidation;

namespace AlaraERP.Application.Inventory;

public sealed class WarehouseRequestValidator : AbstractValidator<WarehouseRequest>
{
    public WarehouseRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(30).Matches("^[A-Za-z0-9][A-Za-z0-9_-]*$")
            .WithMessage("Use letters, digits, '-' or '_'.");
        RuleFor(x => x.Address).MaximumLength(1000);
    }
}

public sealed class UpdateInventorySettingsRequestValidator : AbstractValidator<UpdateInventorySettingsRequest>
{
    public UpdateInventorySettingsRequestValidator() => RuleFor(x => x.LowStockThreshold).InclusiveBetween(0, 1_000_000);
}

public sealed class ReceiveStockRequestValidator : AbstractValidator<ReceiveStockRequest>
{
    public ReceiveStockRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.SupplierName).MaximumLength(200);
        RuleFor(x => x.SupplierReference).MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one product.");
        RuleFor(x => x.Lines.Count).LessThanOrEqualTo(200).When(x => x.Lines is not null);
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 1_000_000);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0).PrecisionScale(12, 4, true).When(l => l.UnitCost.HasValue);
            line.RuleFor(l => l.BatchNumber).MaximumLength(60);
        });
    }
}

public sealed class AdjustStockRequestValidator : AbstractValidator<AdjustStockRequest>
{
    public AdjustStockRequestValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.QuantityChange).NotEqual(0).InclusiveBetween(-1_000_000, 1_000_000);
        RuleFor(x => x.Reason).IsInEnum();
        RuleFor(x => x.Notes).NotEmpty().When(x => x.Reason == AdjustmentReason.Other)
            .WithMessage("Explain the adjustment when the reason is 'Other'.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.BatchId).NotNull().When(x => x.QuantityChange < 0)
            .WithMessage("Choose the batch to remove stock from.");
        RuleFor(x => x.BatchNumber).MaximumLength(60);
    }
}

public sealed class TransferStockRequestValidator : AbstractValidator<TransferStockRequest>
{
    public TransferStockRequestValidator()
    {
        RuleFor(x => x.FromWarehouseId).NotEmpty();
        RuleFor(x => x.ToWarehouseId).NotEmpty().NotEqual(x => x.FromWarehouseId)
            .WithMessage("Source and destination warehouses must differ.");
        RuleFor(x => x.Notes).MaximumLength(2000);
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one product.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).InclusiveBetween(1, 1_000_000);
        });
    }
}
