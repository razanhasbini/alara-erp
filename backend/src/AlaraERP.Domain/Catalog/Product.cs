using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Catalog;

public enum ProductType { Individual, Bundle }

public enum SizeUnit { Ml, G, Pcs }

/// <summary>
/// One sellable item. Scents are separate products; a bundle is a separate product whose stock is
/// held by its components. Stock quantities never live here (see the Inventory module).
/// </summary>
public sealed class Product : IAuditable
{
    private readonly List<BundleItem> _bundleItems = [];
    private readonly List<ProductImage> _images = [];

    private Product() { }

    public Product(ProductType type, string name, string slug, decimal sellingPrice, decimal? compareAtPrice)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Name = name.Trim();
        Slug = slug;
        IsActive = true;
        SetPricing(sellingPrice, compareAtPrice);
    }

    public Guid Id { get; private set; }
    public ProductType Type { get; private set; }
    public Guid? CategoryId { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string? ShortDescription { get; private set; }
    public string? Description { get; private set; }
    public string? HowToUse { get; private set; }
    public string? Ingredients { get; private set; }
    public decimal? SizeValue { get; private set; }
    public SizeUnit? SizeUnit { get; private set; }
    public int? WeightGrams { get; private set; }
    public decimal SellingPrice { get; private set; }
    public decimal? CompareAtPrice { get; private set; }
    /// <summary>
    /// What one unit normally costs ÀLara Line (purchase + landed costs). Pre-fills stock receipts and is the
    /// fallback cost when a batch was received without one. Actual costs live on batches.
    /// </summary>
    public decimal? StandardCost { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public IReadOnlyList<BundleItem> BundleItems => _bundleItems;
    public IReadOnlyList<ProductImage> Images => _images;

    public void UpdateDetails(
        string name, string slug, Guid? categoryId,
        string? shortDescription, string? description, string? howToUse, string? ingredients,
        decimal? sizeValue, SizeUnit? sizeUnit, int? weightGrams)
    {
        if (sizeValue is <= 0)
            throw new DomainException("products.invalid_size", "Size must be greater than zero.");
        if (sizeValue.HasValue != sizeUnit.HasValue)
            throw new DomainException("products.invalid_size", "Size value and unit must be set together.");
        if (weightGrams is < 0)
            throw new DomainException("products.invalid_weight", "Weight cannot be negative.");

        Name = name.Trim();
        Slug = slug;
        CategoryId = categoryId;
        ShortDescription = Clean(shortDescription);
        Description = Clean(description);
        HowToUse = Clean(howToUse);
        Ingredients = Clean(ingredients);
        SizeValue = sizeValue;
        SizeUnit = sizeUnit;
        WeightGrams = weightGrams;
    }

    public void SetPricing(decimal sellingPrice, decimal? compareAtPrice)
    {
        Guard.NotNegative(sellingPrice, "Selling price");
        if (compareAtPrice is { } compare && compare < sellingPrice)
            throw new DomainException("products.invalid_compare_at_price",
                "The 'compare at' price must be at least the selling price.");
        SellingPrice = Guard.Money(sellingPrice);
        CompareAtPrice = compareAtPrice is { } c ? Guard.Money(c) : null;
    }

    public void SetStandardCost(decimal? cost)
    {
        if (cost is < 0)
            throw new DomainException("products.invalid_cost", "Cost cannot be negative.");
        if (cost is not null && Type == ProductType.Bundle)
            throw new DomainException("products.bundle_cost", "A bundle's cost is the cost of its components.");
        StandardCost = cost is { } c ? Math.Round(c, 4, MidpointRounding.AwayFromZero) : null;
    }

    public void SetActive(bool isActive) => IsActive = isActive;

    /// <summary>Replaces the bundle's composition. Components must be individual products, listed once each.</summary>
    public void ReplaceBundleItems(IReadOnlyList<(Product Component, int Quantity)> items)
    {
        if (Type != ProductType.Bundle)
            throw new DomainException("bundles.not_a_bundle", $"'{Name}' is not a bundle.");
        if (items.Count == 0)
            throw new DomainException("bundles.empty", "A bundle needs at least one component.");

        var seen = new HashSet<Guid>();
        foreach (var (component, quantity) in items)
        {
            if (component.Id == Id)
                throw new DomainException("bundles.self_reference", "A bundle cannot contain itself.");
            if (component.Type != ProductType.Individual)
                throw new DomainException("bundles.nested_bundle", $"'{component.Name}' is a bundle; bundles cannot contain bundles.");
            if (!seen.Add(component.Id))
                throw new DomainException("bundles.duplicate_component", $"'{component.Name}' is listed more than once.");
            Guard.Positive(quantity, $"Quantity of '{component.Name}'");
        }

        _bundleItems.Clear();
        _bundleItems.AddRange(items.Select(i => new BundleItem(Id, i.Component.Id, i.Quantity)));
    }

    public ProductImage AddImage(string storageKey, string contentType, long sizeBytes)
    {
        var image = new ProductImage(Id, storageKey, contentType, sizeBytes,
            displayOrder: _images.Count == 0 ? 0 : _images.Max(i => i.DisplayOrder) + 1,
            isPrimary: _images.Count == 0);
        _images.Add(image);
        return image;
    }

    public ProductImage RemoveImage(Guid imageId)
    {
        var image = FindImage(imageId);
        _images.Remove(image);
        if (image.IsPrimary && _images.Count > 0)
            _images.OrderBy(i => i.DisplayOrder).First().IsPrimary = true;
        return image;
    }

    /// <summary>
    /// Swaps a photo for a new file. The new image takes the old one's position and "main" flag; it gets a new id
    /// (and so a new URL), so browsers never keep showing the old picture from their cache.
    /// </summary>
    public (ProductImage Added, ProductImage Removed) ReplaceImage(Guid imageId, string storageKey, string contentType, long sizeBytes)
    {
        var old = FindImage(imageId);
        var replacement = new ProductImage(Id, storageKey, contentType, sizeBytes, old.DisplayOrder, old.IsPrimary);
        _images.Remove(old);
        _images.Add(replacement);
        return (replacement, old);
    }

    /// <summary>
    /// Un-marks the current main photo. Used as the first of two saves when switching the main photo, because the
    /// database allows only one main photo per product and does not guarantee the order of the two updates.
    /// </summary>
    public void ClearPrimaryImage()
    {
        foreach (var image in _images) image.IsPrimary = false;
    }

    public void SetPrimaryImage(Guid imageId)
    {
        var target = FindImage(imageId);
        foreach (var image in _images) image.IsPrimary = image == target;
    }

    public void ReorderImages(IReadOnlyList<Guid> orderedIds)
    {
        if (orderedIds.Count != _images.Count || orderedIds.Except(_images.Select(i => i.Id)).Any())
            throw new DomainException("images.invalid_order", "The new order must list every image of the product exactly once.");
        for (var i = 0; i < orderedIds.Count; i++)
            FindImage(orderedIds[i]).DisplayOrder = i;
    }

    private ProductImage FindImage(Guid id) =>
        _images.FirstOrDefault(i => i.Id == id)
        ?? throw new DomainException("images.not_found", "The image does not belong to this product.");

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class BundleItem
{
    private BundleItem() { }

    internal BundleItem(Guid bundleProductId, Guid componentProductId, int quantity)
    {
        Id = Guid.CreateVersion7();
        BundleProductId = bundleProductId;
        ComponentProductId = componentProductId;
        Quantity = quantity;
    }

    public Guid Id { get; private set; }
    public Guid BundleProductId { get; private set; }
    public Guid ComponentProductId { get; private set; }
    public Product? Component { get; private set; }
    public int Quantity { get; private set; }
}

public sealed class ProductImage : IAuditable
{
    private ProductImage() { }

    internal ProductImage(Guid productId, string storageKey, string contentType, long sizeBytes, int displayOrder, bool isPrimary)
    {
        Id = Guid.CreateVersion7();
        ProductId = productId;
        StorageKey = storageKey;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        DisplayOrder = displayOrder;
        IsPrimary = isPrimary;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public string StorageKey { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public int DisplayOrder { get; internal set; }
    public bool IsPrimary { get; internal set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
