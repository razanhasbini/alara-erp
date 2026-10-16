using AlaraERP.Domain.Catalog;
using FluentValidation;

namespace AlaraERP.Application.Catalog;

internal static class CatalogRules
{
    public const string SlugPattern = "^[a-z0-9]+(-[a-z0-9]+)*$";
}

public sealed class CategoryRequestValidator : AbstractValidator<CategoryRequest>
{
    public CategoryRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Slug).MaximumLength(120).Matches(CatalogRules.SlugPattern).When(x => !string.IsNullOrEmpty(x.Slug))
            .WithMessage("Use lowercase letters, digits and single hyphens.");
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }
}

internal sealed class ProductDetailsValidator : AbstractValidator<ProductDetailsInput>
{
    public ProductDetailsValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Slug).MaximumLength(180).Matches(CatalogRules.SlugPattern).When(x => !string.IsNullOrEmpty(x.Slug))
            .WithMessage("Use lowercase letters, digits and single hyphens.");
        RuleFor(x => x.Barcode).MaximumLength(100);
        RuleFor(x => x.ShortDescription).MaximumLength(1000);
        RuleFor(x => x.Description).MaximumLength(10_000);
        RuleFor(x => x.HowToUse).MaximumLength(5000);
        RuleFor(x => x.Ingredients).MaximumLength(5000);
        RuleFor(x => x.SizeValue).GreaterThan(0).LessThan(100_000).When(x => x.SizeValue.HasValue);
        RuleFor(x => x.SizeUnit).NotNull().When(x => x.SizeValue.HasValue).WithMessage("Choose a unit for the size.");
        RuleFor(x => x.SizeValue).NotNull().When(x => x.SizeUnit.HasValue).WithMessage("Enter the size for the chosen unit.");
        RuleFor(x => x.WeightGrams).InclusiveBetween(0, 100_000).When(x => x.WeightGrams.HasValue);
        RuleFor(x => x.SellingPrice).GreaterThanOrEqualTo(0).LessThan(1_000_000).PrecisionScale(12, 2, true);
        RuleFor(x => x.CompareAtPrice).GreaterThanOrEqualTo(x => x.SellingPrice).When(x => x.CompareAtPrice.HasValue)
            .WithMessage("The 'compare at' price must be at least the selling price.");
        RuleFor(x => x.CompareAtPrice).PrecisionScale(12, 2, true).When(x => x.CompareAtPrice.HasValue);
        RuleFor(x => x.StandardCost).GreaterThanOrEqualTo(0).PrecisionScale(12, 4, true).When(x => x.StandardCost.HasValue);
    }
}

public sealed class CreateProductRequestValidator : AbstractValidator<CreateProductRequest>
{
    public CreateProductRequestValidator()
    {
        Include(new ProductDetailsValidator());
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.BundleItems).NotEmpty().When(x => x.Type == ProductType.Bundle)
            .WithMessage("A bundle needs at least one component.");
        RuleFor(x => x.BundleItems).Empty().When(x => x.Type == ProductType.Individual)
            .WithMessage("Only bundles have components.");
        RuleForEach(x => x.BundleItems).SetValidator(new BundleItemInputValidator());
    }
}

public sealed class UpdateProductRequestValidator : AbstractValidator<UpdateProductRequest>
{
    public UpdateProductRequestValidator() => Include(new ProductDetailsValidator());
}

public sealed class BundleItemInputValidator : AbstractValidator<BundleItemInput>
{
    public BundleItemInputValidator()
    {
        RuleFor(x => x.ComponentProductId).NotEmpty();
        RuleFor(x => x.Quantity).InclusiveBetween(1, 1000);
    }
}

public sealed class ReplaceBundleItemsRequestValidator : AbstractValidator<ReplaceBundleItemsRequest>
{
    public ReplaceBundleItemsRequestValidator()
    {
        RuleFor(x => x.Items).NotEmpty().WithMessage("A bundle needs at least one component.");
        RuleForEach(x => x.Items).SetValidator(new BundleItemInputValidator());
    }
}
