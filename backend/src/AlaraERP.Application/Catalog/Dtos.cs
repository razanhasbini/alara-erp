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
    Guid Id, string Name, string Sku, ProductType Type, Guid? CategoryId, string? CategoryName,
    decimal SellingPrice, decimal? CompareAtPrice, bool IsActive, Guid? PrimaryImageId, int ComponentCount,
    DateTimeOffset UpdatedAt);

public sealed record BundleItemDto(Guid ComponentProductId, string Name, string Sku, int Quantity, decimal UnitPrice, bool IsActive);

public sealed record ProductImageDto(Guid Id, string ContentType, long SizeBytes, int DisplayOrder, bool IsPrimary);

public sealed record ProductDetailDto(
    Guid Id, ProductType Type, string Name, string Slug, string Sku, string? Barcode,
    Guid? CategoryId, string? CategoryName,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice, bool IsActive,
    IReadOnlyList<BundleItemDto> BundleItems,
    /// <summary>Sum of component prices at full price — what the customer saves against.</summary>
    decimal? ComponentsValue,
    IReadOnlyList<ProductImageDto> Images,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record BundleItemInput(Guid ComponentProductId, int Quantity);

public record ProductDetailsInput(
    string Name, string Sku, string? Slug, Guid? CategoryId, string? Barcode,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice);

public sealed record CreateProductRequest(
    ProductType Type, string Name, string Sku, string? Slug, Guid? CategoryId, string? Barcode,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice,
    IReadOnlyList<BundleItemInput>? BundleItems)
    : ProductDetailsInput(Name, Sku, Slug, CategoryId, Barcode, ShortDescription, Description, HowToUse, Ingredients,
        SizeValue, SizeUnit, WeightGrams, SellingPrice, CompareAtPrice);

public sealed record UpdateProductRequest(
    string Name, string Sku, string? Slug, Guid? CategoryId, string? Barcode,
    string? ShortDescription, string? Description, string? HowToUse, string? Ingredients,
    decimal? SizeValue, SizeUnit? SizeUnit, int? WeightGrams,
    decimal SellingPrice, decimal? CompareAtPrice)
    : ProductDetailsInput(Name, Sku, Slug, CategoryId, Barcode, ShortDescription, Description, HowToUse, Ingredients,
        SizeValue, SizeUnit, WeightGrams, SellingPrice, CompareAtPrice);

public sealed record ReplaceBundleItemsRequest(IReadOnlyList<BundleItemInput> Items);

public sealed record ReorderImagesRequest(IReadOnlyList<Guid> ImageIds);

public sealed record ImageContent(Stream Content, string ContentType);
