using AlaraERP.Application.Common;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Catalog;

public sealed class CategoryService(IAlaraDbContext db, IActivityLog activity)
{
    public async Task<IReadOnlyList<CategoryDto>> ListAsync(bool includeInactive, CancellationToken ct)
    {
        var categories = await db.Categories.AsNoTracking()
            .Where(c => includeInactive || c.IsActive)
            .ToListAsync(ct);
        var counts = await db.Products.Where(p => p.CategoryId != null)
            .GroupBy(p => p.CategoryId!.Value)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var names = categories.ToDictionary(c => c.Id, c => c.Name);

        // Depth-first: each parent followed by its children, siblings by sort order then name.
        var byParent = categories.ToLookup(c => c.ParentCategoryId);
        var ordered = new List<Category>();
        void Walk(Guid? parent)
        {
            foreach (var c in byParent[parent].OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            {
                ordered.Add(c);
                Walk(c.Id);
            }
        }
        Walk(null);
        ordered.AddRange(categories.Except(ordered).OrderBy(c => c.Name)); // children of filtered-out parents

        return ordered.Select(c => ToDto(c, names, counts)).ToList();
    }

    public async Task<CategoryDto> GetAsync(Guid id, CancellationToken ct)
    {
        var category = await db.Categories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct)
                       ?? throw new NotFoundException("Category", id);
        var parentName = category.ParentCategoryId is { } pid
            ? await db.Categories.Where(c => c.Id == pid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;
        var count = await db.Products.CountAsync(p => p.CategoryId == id, ct);
        return new CategoryDto(category.Id, category.Name, category.Slug, category.Description, category.ParentCategoryId,
            parentName, category.SortOrder, category.IsActive, count, category.UpdatedAt);
    }

    public async Task<CategoryDto> CreateAsync(CategoryRequest request, CancellationToken ct)
    {
        var slug = await UniqueSlugAsync(request.Slug, request.Name, null, ct);
        await EnsureParentValidAsync(null, request.ParentCategoryId, ct);
        var category = new Category(request.Name, slug, request.ParentCategoryId, request.Description, request.SortOrder);
        if (!request.IsActive) category.Update(request.Name, slug, request.ParentCategoryId, request.Description, request.SortOrder, false);
        db.Categories.Add(category);
        activity.Record(Domain.Activity.ActivityArea.Products, $"added the category {category.Name}", "/categories");
        await db.SaveChangesAsync(ct);
        return await GetAsync(category.Id, ct);
    }

    public async Task<CategoryDto> UpdateAsync(Guid id, CategoryRequest request, CancellationToken ct)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw new NotFoundException("Category", id);
        var slug = await UniqueSlugAsync(request.Slug, request.Name, id, ct);
        await EnsureParentValidAsync(id, request.ParentCategoryId, ct);
        category.Update(request.Name, slug, request.ParentCategoryId, request.Description, request.SortOrder, request.IsActive);
        activity.Record(Domain.Activity.ActivityArea.Products, $"updated the category {category.Name}", "/categories");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Walks up from the proposed parent; reaching the category itself would create a cycle.</summary>
    private async Task EnsureParentValidAsync(Guid? categoryId, Guid? parentId, CancellationToken ct)
    {
        if (parentId is null) return;
        if (parentId == categoryId)
            throw new DomainException("categories.self_parent", "A category cannot be its own parent.");

        var parents = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.ParentCategoryId, ct);
        if (!parents.ContainsKey(parentId.Value))
            throw new NotFoundException("Parent category", parentId.Value);

        var visited = new HashSet<Guid>();
        for (Guid? current = parentId; current is { } c; current = parents.GetValueOrDefault(c))
        {
            if (c == categoryId || !visited.Add(c))
                throw new DomainException("categories.cycle", "This parent would create a loop in the category hierarchy.");
        }
    }

    private async Task<string> UniqueSlugAsync(string? requested, string name, Guid? excludeId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            if (await db.Categories.AnyAsync(c => c.Slug == requested && c.Id != excludeId, ct))
                throw new ConflictException("categories.duplicate_slug", $"Another category already uses the slug '{requested}'.");
            return requested;
        }
        var baseSlug = Text.Slugify(name);
        var slug = baseSlug;
        for (var i = 2; await db.Categories.AnyAsync(c => c.Slug == slug && c.Id != excludeId, ct); i++)
            slug = $"{baseSlug}-{i}";
        return slug;
    }

    private static CategoryDto ToDto(Category c, Dictionary<Guid, string> names, Dictionary<Guid, int> counts) => new(
        c.Id, c.Name, c.Slug, c.Description, c.ParentCategoryId,
        c.ParentCategoryId is { } p ? names.GetValueOrDefault(p) : null,
        c.SortOrder, c.IsActive, counts.GetValueOrDefault(c.Id), c.UpdatedAt);
}
