using AlaraERP.Domain.Common;

namespace AlaraERP.Domain.Catalog;

public sealed class Category : IAuditable
{
    private Category() { }

    public Category(string name, string slug, Guid? parentCategoryId, string? description, int sortOrder)
    {
        Id = Guid.CreateVersion7();
        IsActive = true;
        Update(name, slug, parentCategoryId, description, sortOrder, isActive: true);
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = "";
    public string Slug { get; private set; } = "";
    public string? Description { get; private set; }
    public Guid? ParentCategoryId { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Hierarchy cycles beyond self-parenting are checked by the application service, which sees all categories.</summary>
    public void Update(string name, string slug, Guid? parentCategoryId, string? description, int sortOrder, bool isActive)
    {
        if (parentCategoryId == Id)
            throw new DomainException("categories.self_parent", "A category cannot be its own parent.");
        Name = name.Trim();
        Slug = slug;
        ParentCategoryId = parentCategoryId;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}
