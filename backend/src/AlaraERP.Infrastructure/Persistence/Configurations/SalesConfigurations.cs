using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using AlaraERP.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.ToTable("orders", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_orders_amounts_not_negative",
                "subtotal >= 0 AND discount_total >= 0 AND shipping_total >= 0 AND grand_total >= 0 AND (courier_fee IS NULL OR courier_fee >= 0)");
            t.HasCheckConstraint("ck_orders_discount_within_subtotal", "discount_total <= subtotal");
            t.HasCheckConstraint("ck_orders_grand_total", "grand_total = subtotal - discount_total + shipping_total");
            t.HasCheckConstraint("ck_orders_paid_at", "(payment_status = 'Paid') = (paid_at IS NOT NULL)");
        });
        b.HasKey(o => o.Id);
        b.Property(o => o.OrderNumber).HasMaxLength(40).IsRequired();
        b.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.PaymentStatus).HasConversion<string>().HasMaxLength(30);
        b.Property(o => o.Source).HasConversion<string>().HasMaxLength(20);
        b.Property(o => o.RecipientName).HasMaxLength(150).IsRequired();
        b.Property(o => o.RecipientPhone).HasMaxLength(40).IsRequired();
        b.Property(o => o.RecipientCity).HasMaxLength(100);
        b.Property(o => o.TrackingNumber).HasMaxLength(100);
        b.Property(o => o.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
        foreach (var money in new[] { nameof(Order.Subtotal), nameof(Order.DiscountTotal), nameof(Order.ShippingTotal),
                                      nameof(Order.CourierFee), nameof(Order.GrandTotal) })
            b.Property(money).HasPrecision(12, 2);
        b.Property(o => o.Version).IsRowVersion();
        b.Ignore(o => o.IsFinal);

        b.HasIndex(o => o.OrderNumber).IsUnique();
        b.HasIndex(o => new { o.Status, o.CreatedAt });
        b.HasIndex(o => o.CreatedAt);
        b.HasIndex(o => o.RecipientPhone);
        b.HasIndex(o => new { o.DeliveryCompanyId, o.PaymentStatus });

        b.HasOne<Warehouse>().WithMany().HasForeignKey(o => o.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DeliveryCompany>().WithMany().HasForeignKey(o => o.DeliveryCompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CourierRemittance>().WithMany().HasForeignKey(o => o.CourierRemittanceId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(o => o.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);

        b.HasMany(o => o.Items).WithOne().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(o => o.History).WithOne().HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(o => o.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
        b.Navigation(o => o.History).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.ToTable("order_items", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_order_items_amounts", "unit_price >= 0 AND line_discount >= 0 AND line_total >= 0");
            t.HasCheckConstraint("ck_order_items_line_total", "line_total = unit_price * quantity - line_discount");
        });
        b.HasKey(i => i.Id);
        b.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        b.Property(i => i.Sku).HasMaxLength(80).IsRequired();
        b.Property(i => i.ProductType).HasConversion<string>().HasMaxLength(20);
        b.Property(i => i.UnitPrice).HasPrecision(12, 2);
        b.Property(i => i.LineDiscount).HasPrecision(12, 2);
        b.Property(i => i.LineTotal).HasPrecision(12, 2);
        b.HasIndex(i => i.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(i => i.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(i => i.Components).WithOne().HasForeignKey(c => c.OrderItemId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(i => i.Components).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrderItemComponentConfiguration : IEntityTypeConfiguration<OrderItemComponent>
{
    public void Configure(EntityTypeBuilder<OrderItemComponent> b)
    {
        b.ToTable("order_item_components", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_order_item_components_quantities", "quantity_per_bundle > 0 AND total_quantity > 0");
        });
        b.HasKey(c => c.Id);
        b.Property(c => c.ComponentName).HasMaxLength(200).IsRequired();
        b.Property(c => c.ComponentSku).HasMaxLength(80).IsRequired();
        b.HasIndex(c => c.ComponentProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(c => c.ComponentProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class OrderHistoryEntryConfiguration : IEntityTypeConfiguration<OrderHistoryEntry>
{
    public void Configure(EntityTypeBuilder<OrderHistoryEntry> b)
    {
        b.ToTable("order_history", Schemas.Sales);
        b.HasKey(h => h.Id);
        b.Property(h => h.EventType).HasConversion<string>().HasMaxLength(20);
        b.Property(h => h.OldValue).HasMaxLength(30);
        b.Property(h => h.NewValue).HasMaxLength(30).IsRequired();
        b.HasIndex(h => new { h.OrderId, h.ChangedAt });
        b.HasOne<AppUser>().WithMany().HasForeignKey(h => h.ChangedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class DeliveryCompanyConfiguration : IEntityTypeConfiguration<DeliveryCompany>
{
    public void Configure(EntityTypeBuilder<DeliveryCompany> b)
    {
        b.ToTable("delivery_companies", Schemas.Sales);
        b.HasKey(d => d.Id);
        b.Property(d => d.Name).HasMaxLength(150).IsRequired();
        b.Property(d => d.Phone).HasMaxLength(40);
        b.HasIndex(d => d.Name).IsUnique();
    }
}

internal sealed class CourierRemittanceConfiguration : IEntityTypeConfiguration<CourierRemittance>
{
    public void Configure(EntityTypeBuilder<CourierRemittance> b)
    {
        b.ToTable("courier_remittances", Schemas.Sales, t =>
        {
            t.HasCheckConstraint("ck_courier_remittances_amounts", "amount_collected >= 0 AND courier_fees >= 0 AND order_count > 0");
            t.HasCheckConstraint("ck_courier_remittances_net", "net_amount = amount_collected - courier_fees");
        });
        b.HasKey(r => r.Id);
        b.Property(r => r.RemittanceNumber).HasMaxLength(40).IsRequired();
        b.Property(r => r.AmountCollected).HasPrecision(12, 2);
        b.Property(r => r.CourierFees).HasPrecision(12, 2);
        b.Property(r => r.NetAmount).HasPrecision(12, 2);
        b.HasIndex(r => r.RemittanceNumber).IsUnique();
        b.HasIndex(r => new { r.DeliveryCompanyId, r.ReceivedAt });
        b.HasOne<DeliveryCompany>().WithMany().HasForeignKey(r => r.DeliveryCompanyId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(r => r.RecordedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
