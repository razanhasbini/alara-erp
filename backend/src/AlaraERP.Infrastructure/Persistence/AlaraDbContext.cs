using AlaraERP.Application.Common;
using AlaraERP.Domain.Activity;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Finance;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using AlaraERP.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

public class AlaraDbContext(DbContextOptions<AlaraDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options), IAlaraDbContext
{
    public const string OrderNumberSequence = "order_number_seq";
    public const string ReceiptNumberSequence = "receipt_number_seq";
    public const string TransferNumberSequence = "transfer_number_seq";

    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BundleItem> BundleItems => Set<BundleItem>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<ProductInventorySettings> ProductInventorySettings => Set<ProductInventorySettings>();
    public DbSet<InventoryFeatureSettings> InventoryFeatureSettings => Set<InventoryFeatureSettings>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<BatchBalance> BatchBalances => Set<BatchBalance>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<LowStockAlert> LowStockAlerts => Set<LowStockAlert>();
    public DbSet<StockReceipt> StockReceipts => Set<StockReceipt>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<Box> Boxes => Set<Box>();
    public DbSet<BoxMovement> BoxMovements => Set<BoxMovement>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<OrderHistoryEntry> OrderHistory => Set<OrderHistoryEntry>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();

    public DbSet<AlaraERP.Domain.Notifications.Notification> Notifications => Set<AlaraERP.Domain.Notifications.Notification>();
    public DbSet<AlaraERP.Domain.Notifications.NotificationRead> NotificationReads => Set<AlaraERP.Domain.Notifications.NotificationRead>();
    public DbSet<AlaraERP.Domain.Notifications.WhatsAppMessage> WhatsAppMessages => Set<AlaraERP.Domain.Notifications.WhatsAppMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ConfigureIdentity(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AlaraDbContext).Assembly);

        // Domain entities create their own ids (UUIDv7). Telling EF the keys are never store-generated makes it
        // treat new children reached through navigations (order lines, history, images…) as inserts, not updates.
        foreach (var entity in builder.Model.GetEntityTypes().Where(e => e.ClrType.Namespace?.StartsWith("AlaraERP.Domain") == true))
        {
            var key = entity.FindPrimaryKey();
            if (key is { Properties: [{ ClrType: var t } single] } && t == typeof(Guid))
                single.ValueGenerated = Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never;
        }

        builder.HasSequence<long>(OrderNumberSequence, Schemas.Sales);
        builder.HasSequence<long>(ReceiptNumberSequence, Schemas.Inventory);
        builder.HasSequence<long>(TransferNumberSequence, Schemas.Inventory);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<AppUser>(e =>
        {
            e.ToTable("users", Schemas.Identity);
            e.Property(u => u.FirstName).HasMaxLength(100).IsRequired();
            e.Property(u => u.LastName).HasMaxLength(100).IsRequired();
            e.Property(u => u.IsActive).HasDefaultValue(true);
            e.HasIndex(u => u.IsActive);
            e.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ux_users_normalized_user_name").IsUnique();
            // Identity checks email uniqueness in code; the unique index makes it race-safe.
            e.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email").IsUnique();
        });
        builder.Entity<AppRole>(e =>
        {
            e.ToTable("roles", Schemas.Identity);
            e.Property(r => r.Description).HasMaxLength(500).IsRequired();
            e.HasIndex(r => r.NormalizedName).HasDatabaseName("ux_roles_normalized_name").IsUnique();
        });
        builder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", Schemas.Identity);
        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", Schemas.Identity);
        builder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", Schemas.Identity);
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", Schemas.Identity);
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", Schemas.Identity);
    }
}
