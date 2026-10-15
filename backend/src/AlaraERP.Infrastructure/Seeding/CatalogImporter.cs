using System.Text.Json;
using AlaraERP.Application.Common;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using AlaraERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AlaraERP.Infrastructure.Seeding;

/// <summary>
/// Loads the real ÀLara Line catalogue (categories, products, bundles) from a JSON file.
/// Idempotent and additive: existing categories/products (matched by slug/SKU) are left untouched,
/// so edits made in the ERP are never overwritten. Creates no stock, costs or orders.
/// </summary>
public sealed class CatalogImporter(AlaraDbContext db, ILogger<CatalogImporter> logger)
{
    public sealed record Result(int CategoriesCreated, int ProductsCreated, int BundlesCreated, int Skipped, bool WarehouseCreated);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<Result> ImportAsync(string path, CancellationToken ct)
    {
        var file = JsonSerializer.Deserialize<CatalogFile>(await File.ReadAllTextAsync(path, ct), Json)
                   ?? throw new InvalidOperationException("The catalogue file is empty.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        int categoriesCreated = 0, productsCreated = 0, bundlesCreated = 0, skipped = 0;

        // Categories: parents before children.
        var categories = await db.Categories.ToDictionaryAsync(c => c.Slug, ct);
        foreach (var c in file.Categories.OrderBy(c => c.Parent is null ? 0 : 1))
        {
            if (categories.ContainsKey(c.Slug)) continue;
            Guid? parentId = c.Parent is null ? null : categories[c.Parent].Id;
            var category = new Category(c.Name, c.Slug, parentId, c.Description, c.SortOrder);
            db.Categories.Add(category);
            categories[c.Slug] = category;
            categoriesCreated++;
        }
        await db.SaveChangesAsync(ct);

        var existing = await db.Products.Select(p => p.Sku).ToHashSetAsync(ct);
        var bySku = await db.Products.ToDictionaryAsync(p => p.Sku, ct);

        foreach (var p in file.Products)
        {
            if (existing.Contains(p.Sku)) { skipped++; continue; }
            var product = new Product(ProductType.Individual, p.Name, p.Slug, p.Sku, p.SellingPrice, p.CompareAtPrice);
            product.UpdateDetails(p.Name, p.Slug, p.Sku, categories[p.Category].Id, null, p.ShortDescription, p.Description,
                p.HowToUse, p.Ingredients, null, null, p.WeightGrams);
            db.Products.Add(product);
            db.ProductInventorySettings.Add(new ProductInventorySettings(product.Id, lowStockThreshold: 0, p.TracksExpiry));
            bySku[p.Sku] = product;
            productsCreated++;
        }
        await db.SaveChangesAsync(ct);

        foreach (var b in file.Bundles)
        {
            if (existing.Contains(b.Sku)) { skipped++; continue; }
            var bundle = new Product(ProductType.Bundle, b.Name, b.Slug, b.Sku, b.SellingPrice, b.CompareAtPrice);
            bundle.UpdateDetails(b.Name, b.Slug, b.Sku, categories[b.Category].Id, null, b.ShortDescription, b.Description,
                null, null, null, null, null);
            bundle.ReplaceBundleItems(b.Components.Select(c => (bySku[c.Sku], c.Quantity)).ToList());
            db.Products.Add(bundle);
            bundlesCreated++;
        }

        var warehouseCreated = false;
        if (!await db.Warehouses.AnyAsync(ct))
        {
            db.Warehouses.Add(new Warehouse("Main Warehouse", "MAIN", null));
            warehouseCreated = true;
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var result = new Result(categoriesCreated, productsCreated, bundlesCreated, skipped, warehouseCreated);
        logger.LogInformation("Catalogue import: {Result}", result);
        return result;
    }

    private sealed record CatalogFile(List<CategoryRow> Categories, List<ProductRow> Products, List<BundleRow> Bundles);
    private sealed record CategoryRow(string Slug, string Name, string? Parent, int SortOrder, string? Description);
    private sealed record ProductRow(string Sku, string Name, string Slug, string Category, decimal SellingPrice, decimal? CompareAtPrice,
        string? ShortDescription, string? Description, string? HowToUse, string? Ingredients, int? WeightGrams, bool TracksExpiry);
    private sealed record BundleRow(string Sku, string Name, string Slug, string Category, decimal SellingPrice, decimal? CompareAtPrice,
        string? ShortDescription, string? Description, List<ComponentRow> Components);
    private sealed record ComponentRow(string Sku, int Quantity);
}
