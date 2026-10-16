using AlaraERP.Domain.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.ToTable("categories", Schemas.Catalog, t =>
            t.HasCheckConstraint("ck_categories_not_self_parent", "parent_category_id IS NULL OR parent_category_id <> id"));
        b.HasKey(c => c.Id);
        b.Property(c => c.Name).HasMaxLength(100).IsRequired();
        b.Property(c => c.Slug).HasMaxLength(120).IsRequired();
        b.HasIndex(c => c.Slug).IsUnique();
        b.HasOne<Category>().WithMany().HasForeignKey(c => c.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(c => c.ParentCategoryId);
    }
}

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.ToTable("products", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_products_type", "type IN ('Individual', 'Bundle')");
            t.HasCheckConstraint("ck_products_selling_price", "selling_price >= 0");
            t.HasCheckConstraint("ck_products_compare_at_price", "compare_at_price IS NULL OR compare_at_price >= selling_price");
            t.HasCheckConstraint("ck_products_size", "(size_value IS NULL) = (size_unit IS NULL) AND (size_value IS NULL OR size_value > 0)");
            t.HasCheckConstraint("ck_products_weight", "weight_grams IS NULL OR weight_grams >= 0");
            t.HasCheckConstraint("ck_products_standard_cost", "standard_cost IS NULL OR standard_cost >= 0");
        });
        b.HasKey(p => p.Id);
        b.Property(p => p.Type).HasConversion<string>().HasMaxLength(20);
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Slug).HasMaxLength(180).IsRequired();
        b.Property(p => p.SizeValue).HasPrecision(10, 2);
        b.Property(p => p.SizeUnit).HasConversion<string>().HasMaxLength(10);
        b.Property(p => p.SellingPrice).HasPrecision(12, 2);
        b.Property(p => p.CompareAtPrice).HasPrecision(12, 2);
        b.Property(p => p.StandardCost).HasPrecision(12, 4);
        b.HasIndex(p => p.Slug).IsUnique();
        b.HasIndex(p => new { p.IsActive, p.Name });
        b.HasIndex(p => p.CategoryId);
        b.HasOne<Category>().WithMany().HasForeignKey(p => p.CategoryId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(p => p.BundleItems).WithOne().HasForeignKey(i => i.BundleProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(p => p.Images).WithOne().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(p => p.BundleItems).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(p => p.Images).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class BundleItemConfiguration : IEntityTypeConfiguration<BundleItem>
{
    public void Configure(EntityTypeBuilder<BundleItem> b)
    {
        b.ToTable("bundle_items", Schemas.Catalog, t =>
        {
            t.HasCheckConstraint("ck_bundle_items_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_bundle_items_not_self", "bundle_product_id <> component_product_id");
        });
        b.HasKey(i => i.Id);
        b.HasIndex(i => new { i.BundleProductId, i.ComponentProductId }).IsUnique();
        b.HasIndex(i => i.ComponentProductId);
        b.HasOne(i => i.Component).WithMany().HasForeignKey(i => i.ComponentProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class ProductImageConfiguration : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> b)
    {
        b.ToTable("product_images", Schemas.Catalog, t => t.HasCheckConstraint("ck_product_images_size", "size_bytes > 0"));
        b.HasKey(i => i.Id);
        b.Property(i => i.StorageKey).HasMaxLength(300).IsRequired();
        b.Property(i => i.ContentType).HasMaxLength(50).IsRequired();
        b.HasIndex(i => i.StorageKey).IsUnique();
        b.HasIndex(i => i.ProductId).IsUnique().HasFilter("is_primary").HasDatabaseName("ux_product_images_one_primary");
        b.HasIndex(i => new { i.ProductId, i.DisplayOrder });
    }
}
