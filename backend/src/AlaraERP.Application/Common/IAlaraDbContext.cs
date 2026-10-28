using AlaraERP.Domain.Activity;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Finance;
using AlaraERP.Domain.Inventory;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AlaraERP.Application.Common;

/// <summary>
/// The application's view of the database. Services use EF Core directly (no generic repositories);
/// module boundaries are enforced by architecture tests on which entity types each module touches.
/// </summary>
public interface IAlaraDbContext
{
    // catalog
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<BundleItem> BundleItems { get; }
    DbSet<ProductImage> ProductImages { get; }

    // inventory
    DbSet<Warehouse> Warehouses { get; }
    DbSet<ProductInventorySettings> ProductInventorySettings { get; }
    DbSet<InventoryFeatureSettings> InventoryFeatureSettings { get; }
    DbSet<Batch> Batches { get; }
    DbSet<StockBalance> StockBalances { get; }
    DbSet<BatchBalance> BatchBalances { get; }
    DbSet<StockReservation> StockReservations { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<LowStockAlert> LowStockAlerts { get; }
    DbSet<StockReceipt> StockReceipts { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<Box> Boxes { get; }
    DbSet<BoxMovement> BoxMovements { get; }

    // sales
    DbSet<Customer> Customers { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<OrderHistoryEntry> OrderHistory { get; }
    DbSet<Expense> Expenses { get; }
    DbSet<ActivityEntry> ActivityEntries { get; }

    // notifications
    DbSet<AlaraERP.Domain.Notifications.Notification> Notifications { get; }
    DbSet<AlaraERP.Domain.Notifications.NotificationRead> NotificationReads { get; }
    DbSet<AlaraERP.Domain.Notifications.WhatsAppMessage> WhatsAppMessages { get; }

    DatabaseFacade Database { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Human-readable, gap-tolerant document numbers backed by PostgreSQL sequences.</summary>
public interface IDocumentNumbers
{
    Task<string> NextOrderNumberAsync(CancellationToken ct);
    Task<string> NextReceiptNumberAsync(CancellationToken ct);
    Task<string> NextTransferNumberAsync(CancellationToken ct);
}

/// <summary>Resolves employee ids to display names for audit columns ("created by", "performed by").</summary>
public interface IUserDirectory
{
    Task<IReadOnlyDictionary<Guid, string>> GetDisplayNamesAsync(IEnumerable<Guid?> userIds, CancellationToken ct);
}

/// <summary>Stores binary files (product images) outside the database.</summary>
public interface IFileStorage
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}
