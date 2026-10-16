using AlaraERP.Domain.Catalog;

namespace AlaraERP.Application.Catalog;

public sealed record CategoryDto(
    Guid Id, string Name, string Slug, string? Description, Guid? ParentCategoryId, string? ParentName,
    int SortOrder, bool IsActive, int ProductCount, DateTimeOffset UpdatedAt);

public sealed record CategoryRequest(
    string Name, string? Slug, string? Description, Guid? ParentCategoryId, int SortOrder = 0, bool IsActive = true);

public sealed record ProductListQuery(
    string? Search, Guid? CategoryId, ProductType? Type, bool? IsActive, string? Sort, int Page = 1, int PageSize = 25);

public sealed record ProductListItemDto(
    Guid Id, string Name, ProductType Type, Guid? CategoryId, string? CategoryName,
    decimal SellingPrice, decimal? CompareAtPrice, bool IsActive, Guid? PrimaryImageId, int ComponentCount,
    DateTimeOffset UpdatedAt);

public sealed record BundleItemDto(Guid ComponentProductId, string Name, int Quantity, decimal UnitPrice, bool IsActive);

public sealed record ProductImageDto(Guid Id, string ContentType, long SizeBytes, int DisplayOrder, bool IsPrimary);

public sealed record ProductDetailDto(
    Guid Id, ProductType Type, string Name, string Slug,
    Guid? CategoryId, string? CategoryName,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice,
    /// <summary>Only returned to employees with costs.view; null otherwise.</summary>
    decimal? StandardCost,
    bool IsActive,
    IReadOnlyList<BundleItemDto> BundleItems,
    /// <summary>Sum of component prices at full price — what the customer saves against.</summary>
    decimal? ComponentsValue,
    IReadOnlyList<ProductImageDto> Images,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record BundleItemInput(Guid ComponentProductId, int Quantity);

public record ProductDetailsInput(
    string Name, string? Slug, Guid? CategoryId,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice, decimal? StandardCost = null);

public sealed record CreateProductRequest(
    ProductType Type, string Name, string? Slug, Guid? CategoryId,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice,
    IReadOnlyList<BundleItemInput>? BundleItems,
    decimal? StandardCost = null)
    : ProductDetailsInput(Name, Slug, CategoryId, ShortDescription, Description, HowToUse, Ingredients,
        SizeValue, SizeUnit, WeightGrams, SellingPrice, CompareAtPrice, StandardCost);

public sealed record UpdateProductRequest(
    string Name, string? Slug, Guid? CategoryId,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice,
    /// <summary>Ignored (left unchanged) for employees without costs.view, so they cannot erase a cost they can't see.</summary>
    decimal? StandardCost = null)
    : ProductDetailsInput(Name, Slug, CategoryId, ShortDescription, Description, HowToUse, Ingredients,
        SizeValue, SizeUnit, WeightGrams, SellingPrice, CompareAtPrice, StandardCost);

public sealed record ReplaceBundleItemsRequest(IReadOnlyList<BundleItemInput> Items);

public sealed record ReorderImagesRequest(IReadOnlyList<Guid> ImageIds);

public sealed record ImageContent(Stream Content, string ContentType);
