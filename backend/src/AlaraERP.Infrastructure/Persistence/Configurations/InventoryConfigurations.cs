using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using AlaraERP.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AlaraERP.Infrastructure.Persistence.Configurations;

internal sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.ToTable("warehouses", Schemas.Inventory);
        b.HasKey(w => w.Id);
        b.Property(w => w.Name).HasMaxLength(150).IsRequired();
        b.Property(w => w.Code).HasMaxLength(30).IsRequired();
        b.HasIndex(w => w.Code).IsUnique();
    }
}

internal sealed class ProductInventorySettingsConfiguration : IEntityTypeConfiguration<ProductInventorySettings>
{
    public void Configure(EntityTypeBuilder<ProductInventorySettings> b)
    {
        b.ToTable("product_settings", Schemas.Inventory, t => t.HasCheckConstraint("ck_product_settings_threshold", "low_stock_threshold >= 0"));
        b.HasKey(s => s.ProductId);
        b.HasOne<Product>().WithOne().HasForeignKey<ProductInventorySettings>(s => s.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.ToTable("batches", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.BatchNumber).HasMaxLength(60).IsRequired();
        b.HasIndex(x => new { x.ProductId, x.BatchNumber }).IsUnique();
        b.HasIndex(x => x.ExpiryDate);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> b)
    {
        b.ToTable("stock_balances", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_balances_on_hand", "quantity_on_hand >= 0");
            t.HasCheckConstraint("ck_stock_balances_reserved", "quantity_reserved >= 0 AND quantity_reserved <= quantity_on_hand");
        });
        b.HasKey(x => new { x.WarehouseId, x.ProductId });
        b.Ignore(x => x.Available);
        b.HasIndex(x => x.ProductId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BatchBalanceConfiguration : IEntityTypeConfiguration<BatchBalance>
{
    public void Configure(EntityTypeBuilder<BatchBalance> b)
    {
        b.ToTable("batch_balances", Schemas.Inventory, t => t.HasCheckConstraint("ck_batch_balances_on_hand", "quantity_on_hand >= 0"));
        b.HasKey(x => new { x.WarehouseId, x.BatchId });
        b.HasIndex(x => x.BatchId);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> b)
    {
        b.ToTable("stock_reservations", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_reservations_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_stock_reservations_closed", "(status = 'Active') = (closed_at IS NULL)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => new { x.OrderId, x.Status });
        b.HasIndex(x => new { x.OrderId, x.ProductId }).IsUnique().HasFilter("status = 'Active'")
            .HasDatabaseName("ux_stock_reservations_one_active_per_product");
        b.HasOne<Order>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> b)
    {
        b.ToTable("stock_movements", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_movements_nonzero", "quantity_change <> 0");
            t.HasCheckConstraint("ck_stock_movements_sign",
                "(type IN ('Receipt', 'Return', 'AdjustmentIncrease', 'TransferIn')) = (quantity_change > 0)");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);
        b.Property(x => x.ReferenceType).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.ReferenceNumber).HasMaxLength(40);
        b.HasIndex(x => x.CreatedAt);
        b.HasIndex(x => new { x.ProductId, x.CreatedAt });
        b.HasIndex(x => new { x.WarehouseId, x.CreatedAt });
        b.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockReceiptConfiguration : IEntityTypeConfiguration<StockReceipt>
{
    public void Configure(EntityTypeBuilder<StockReceipt> b)
    {
        b.ToTable("stock_receipts", Schemas.Inventory);
        b.HasKey(x => x.Id);
        b.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.SupplierName).HasMaxLength(200);
        b.Property(x => x.SupplierReference).HasMaxLength(100);
        b.HasIndex(x => x.ReceiptNumber).IsUnique();
        b.HasIndex(x => x.ReceivedAt);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.ReceiptId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockReceiptLineConfiguration : IEntityTypeConfiguration<StockReceiptLine>
{
    public void Configure(EntityTypeBuilder<StockReceiptLine> b)
    {
        b.ToTable("stock_receipt_lines", Schemas.Inventory, t =>
        {
            t.HasCheckConstraint("ck_stock_receipt_lines_quantity", "quantity > 0");
            t.HasCheckConstraint("ck_stock_receipt_lines_unit_cost", "unit_cost IS NULL OR unit_cost >= 0");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.UnitCost).HasPrecision(12, 4);
        b.HasIndex(x => x.ProductId);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.ToTable("stock_transfers", Schemas.Inventory, t =>
            t.HasCheckConstraint("ck_stock_transfers_distinct_warehouses", "from_warehouse_id <> to_warehouse_id"));
        b.HasKey(x => x.Id);
        b.Property(x => x.TransferNumber).HasMaxLength(40).IsRequired();
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TransferNumber).IsUnique();
        b.HasIndex(x => x.CreatedAt);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.FromWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.ToWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AppUser>().WithMany().HasForeignKey(x => x.PerformedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.TransferId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> b)
    {
        b.ToTable("stock_transfer_lines", Schemas.Inventory, t => t.HasCheckConstraint("ck_stock_transfer_lines_quantity", "quantity > 0"));
        b.HasKey(x => x.Id);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.Restrict);
    }
}
