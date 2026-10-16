using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Catalog;

public sealed class ProductService(IAlaraDbContext db, ICurrentUser currentUser)
{
    public async Task<PagedResult<ProductListItemDto>> ListAsync(ProductListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = db.Products.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Text.ContainsPattern(query.Search);
            q = q.Where(p => EF.Functions.Like(p.Name.ToLower(), term, "\\")
                          || (p.Barcode != null && EF.Functions.Like(p.Barcode.ToLower(), term, "\\")));
        }
        if (query.CategoryId is { } categoryId)
            q = q.Where(p => p.CategoryId == categoryId
                          || db.Categories.Any(c => c.Id == p.CategoryId && c.ParentCategoryId == categoryId));
        if (query.Type is { } type) q = q.Where(p => p.Type == type);
        if (query.IsActive is { } active) q = q.Where(p => p.IsActive == active);

        IOrderedQueryable<Product> ordered = query.Sort switch
        {
            "-name" => q.OrderByDescending(p => p.Name),
            "price" => q.OrderBy(p => p.SellingPrice).ThenBy(p => p.Name),
            "-price" => q.OrderByDescending(p => p.SellingPrice).ThenBy(p => p.Name),
            "updated" => q.OrderBy(p => p.UpdatedAt),
            "-updated" => q.OrderByDescending(p => p.UpdatedAt),
            _ => q.OrderBy(p => p.Name),
        };

        var total = await q.CountAsync(ct);
        var items = await ordered.ThenBy(p => p.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(p => new ProductListItemDto(
                p.Id, p.Name, p.Type, p.CategoryId,
                db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                p.SellingPrice, p.CompareAtPrice, p.IsActive,
                p.Images.Where(i => i.IsPrimary).Select(i => (Guid?)i.Id).FirstOrDefault(),
                p.BundleItems.Count, p.UpdatedAt))
            .ToListAsync(ct);
        return new PagedResult<ProductListItemDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<ProductDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await db.Products.AsNoTracking()
                    .Include(x => x.Images)
                    .Include(x => x.BundleItems).ThenInclude(b => b.Component)
                    .FirstOrDefaultAsync(x => x.Id == id, ct)
                ?? throw new NotFoundException("Product", id);
        var categoryName = p.CategoryId is { } cid
            ? await db.Categories.Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;

        var items = p.BundleItems
            .Select(b => new BundleItemDto(b.ComponentProductId, b.Component!.Name, b.Quantity,
                b.Component.SellingPrice, b.Component.IsActive))
            .OrderBy(b => b.Name)
            .ToList();

        return new ProductDetailDto(
            p.Id, p.Type, p.Name, p.Slug, p.Barcode, p.CategoryId, categoryName,
            p.ShortDescription, p.Description, p.HowToUse, p.Ingredients,
            p.SizeValue, p.SizeUnit, p.WeightGrams, p.SellingPrice, p.CompareAtPrice,
            currentUser.HasPermission(Permissions.Costs.View) ? p.StandardCost : null, p.IsActive,
            items,
            p.Type == ProductType.Bundle ? items.Sum(i => i.UnitPrice * i.Quantity) : null,
            p.Images.OrderBy(i => i.DisplayOrder)
                .Select(i => new ProductImageDto(i.Id, i.ContentType, i.SizeBytes, i.DisplayOrder, i.IsPrimary)).ToList(),
            p.CreatedAt, p.UpdatedAt);
    }

    public async Task<ProductDetailDto> CreateAsync(CreateProductRequest request, CancellationToken ct)
    {
        var slug = await UniqueSlugAsync(request.Slug, request.Name, null, ct);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var product = new Product(request.Type, request.Name, slug, request.SellingPrice, request.CompareAtPrice);
        ApplyDetails(product, request, slug);
        if (request.StandardCost is not null)
        {
            EnsureCanSetCost();
            product.SetStandardCost(request.StandardCost);
        }
        if (request.Type == ProductType.Bundle)
            product.ReplaceBundleItems(await ResolveComponentsAsync(request.BundleItems ?? [], ct));

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return await GetAsync(product.Id, ct);
    }

    public async Task<ProductDetailDto> UpdateAsync(Guid id, UpdateProductRequest request, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);
        var slug = await UniqueSlugAsync(request.Slug, request.Name, id, ct);
        await EnsureCategoryExistsAsync(request.CategoryId, ct);

        var priceChanged = product.SellingPrice != request.SellingPrice || product.CompareAtPrice != request.CompareAtPrice;
        if (priceChanged && !currentUser.HasPermission(Permissions.Products.ManagePricing))
            throw new ForbiddenOperationException("products.pricing_not_allowed", "You are not allowed to change product prices.");

        ApplyDetails(product, request, slug);
        product.SetPricing(request.SellingPrice, request.CompareAtPrice);
        if (currentUser.HasPermission(Permissions.Costs.View) && request.StandardCost != product.StandardCost)
        {
            EnsureCanSetCost();
            product.SetStandardCost(request.StandardCost);
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ProductDetailDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Product", id);
        product.SetActive(isActive);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Hard delete is only for products that were never used. Stock, order and bundle history reference the
    /// product through RESTRICT foreign keys, so the database refuses the delete; we translate that into advice.
    /// (Catalog deliberately does not query the Inventory or Sales modules.)
    /// </summary>
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Images).FirstOrDefaultAsync(p => p.Id == id, ct)
                      ?? throw new NotFoundException("Product", id);
        if (product.Images.Count > 0)
            throw new ConflictException("products.has_images", "Remove the product's images before deleting it.");

        db.Products.Remove(product);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new ConflictException("products.in_use",
                "This product has stock, order or bundle history and cannot be deleted. Deactivate it instead.");
        }
    }

    public async Task<ProductDetailDto> ReplaceBundleItemsAsync(Guid id, ReplaceBundleItemsRequest request, CancellationToken ct)
    {
        var bundle = await db.Products.Include(p => p.BundleItems).FirstOrDefaultAsync(p => p.Id == id, ct)
                     ?? throw new NotFoundException("Product", id);
        bundle.ReplaceBundleItems(await ResolveComponentsAsync(request.Items, ct));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ---- helpers ---------------------------------------------------------------------------

    /// <summary>Setting a cost requires seeing costs and managing prices (Managers, Super Admins).</summary>
    private void EnsureCanSetCost()
    {
        if (!currentUser.HasPermission(Permissions.Costs.View) || !currentUser.HasPermission(Permissions.Products.ManagePricing))
            throw new ForbiddenOperationException("products.cost_not_allowed", "You are not allowed to set product costs.");
    }

    private static void ApplyDetails(Product product, ProductDetailsInput r, string slug) =>
        product.UpdateDetails(r.Name, slug, r.CategoryId, r.Barcode, r.ShortDescription, r.Description,
            r.HowToUse, r.Ingredients, r.SizeValue, r.SizeUnit, r.WeightGrams);

    private async Task<List<(Product, int)>> ResolveComponentsAsync(IReadOnlyList<BundleItemInput> items, CancellationToken ct)
    {
        var ids = items.Select(i => i.ComponentProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        return items.Select(i => (
                products.GetValueOrDefault(i.ComponentProductId)
                ?? throw new DomainException("bundles.unknown_component", $"Component product '{i.ComponentProductId}' does not exist."),
                i.Quantity))
            .ToList();
    }

    private async Task EnsureCategoryExistsAsync(Guid? categoryId, CancellationToken ct)
    {
        if (categoryId is { } id && !await db.Categories.AnyAsync(c => c.Id == id, ct))
            throw new NotFoundException("Category", id);
    }

    private async Task<string> UniqueSlugAsync(string? requested, string name, Guid? excludeId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (await db.Products.AnyAsync(p => p.Slug == requested && p.Id != excludeId, ct))
                throw new ConflictException("products.duplicate_slug", $"Another product already uses the slug '{requested}'.");
            return requested;
        }
        var baseSlug = Text.Slugify(name);
        var slug = baseSlug;
        for (var i = 2; await db.Products.AnyAsync(p => p.Slug == slug && p.Id != excludeId, ct); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }
}
