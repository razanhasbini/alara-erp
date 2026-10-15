using AlaraERP.Application.Common;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

public sealed record StockableProduct(Guid Id, string Name, string Sku, bool TracksExpiry)
{
    public string Label => $"{Name} ({Sku})";
}

/// <summary>
/// Shared building blocks for every stock change: batch lookup/creation, FEFO picking and ledger entries.
/// Callers must already hold the <see cref="IStockLocks"/> lock for the affected (warehouse, product) pairs.
/// </summary>
public sealed class StockLedger(IAlaraDbContext db, BusinessClock clock, ICurrentUser currentUser)
{
    public DateTimeOffset Now => clock.UtcNow;
    public DateOnly Today => clock.Today;

    public async Task<Dictionary<Guid, StockableProduct>> LoadStockableProductsAsync(IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Name, p.Sku, p.Type,
                TracksExpiry = db.ProductInventorySettings.Where(s => s.ProductId == p.Id).Select(s => (bool?)s.TracksExpiry).FirstOrDefault()
                               ?? InventorySettingsService.DefaultTracksExpiry,
            })
            .ToListAsync(ct);

        var missing = ids.Except(products.Select(p => p.Id)).FirstOrDefault();
        if (missing != Guid.Empty) throw new NotFoundException("Product", missing);
        var bundle = products.FirstOrDefault(p => p.Type == ProductType.Bundle);
        if (bundle is not null)
            throw new DomainException("inventory.bundle_not_stocked",
                $"'{bundle.Name}' is a bundle; receive or move its component products instead.");

        return products.ToDictionary(p => p.Id, p => new StockableProduct(p.Id, p.Name, p.Sku, p.TracksExpiry));
    }

    public async Task<Warehouse> RequireActiveWarehouseAsync(Guid id, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id, ct)
                        ?? throw new NotFoundException("Warehouse", id);
        if (!warehouse.IsActive)
            throw new DomainException("warehouses.inactive", $"Warehouse {warehouse.Code} is inactive.");
        return warehouse;
    }

    /// <summary>Tracked batch balances (with their batch) for the given products in the given warehouses.</summary>
    public async Task<Dictionary<(Guid WarehouseId, Guid BatchId), BatchBalance>> LoadBatchBalancesAsync(
        IEnumerable<Guid> warehouseIds, IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var w = warehouseIds.Distinct().ToList();
        var p = productIds.Distinct().ToList();
        return await db.BatchBalances.Include(b => b.Batch)
            .Where(b => w.Contains(b.WarehouseId) && p.Contains(b.Batch!.ProductId))
            .ToDictionaryAsync(b => (b.WarehouseId, b.BatchId), ct);
    }

    public BatchBalance GetOrCreateBatchBalance(Dictionary<(Guid, Guid), BatchBalance> balances, Guid warehouseId, Batch batch)
    {
        if (balances.TryGetValue((warehouseId, batch.Id), out var existing)) return existing;
        var created = new BatchBalance(warehouseId, batch.Id);
        db.BatchBalances.Add(created);
        balances[(warehouseId, batch.Id)] = created;
        return created;
    }

    /// <summary>
    /// Finds the batch to put new units into, creating it if needed. Tracked products require an expiry date;
    /// already-expired stock cannot be received.
    /// </summary>
    public async Task<Batch> ResolveIncomingBatchAsync(StockableProduct product, string? batchNumber, DateOnly? expiry,
        string defaultBatchNumber, CancellationToken ct)
    {
        var number = string.IsNullOrWhiteSpace(batchNumber) ? defaultBatchNumber : batchNumber.Trim();
        var existing = db.Batches.Local.FirstOrDefault(b => b.ProductId == product.Id && b.BatchNumber == number)
                       ?? await db.Batches.FirstOrDefaultAsync(b => b.ProductId == product.Id && b.BatchNumber == number, ct);
        if (existing is not null)
        {
            if (expiry is { } e && existing.ExpiryDate != e)
                throw new ConflictException("inventory.batch_expiry_mismatch",
                    $"Batch {number} of {product.Label} already exists with expiry {existing.ExpiryDate?.ToString("yyyy-MM-dd") ?? "none"}.");
            return existing;
        }

        if (product.TracksExpiry && expiry is null)
            throw new DomainException("inventory.expiry_required", $"{product.Label} tracks expiry: enter the expiry date.");
        if (expiry is { } exp && exp < Today)
            throw new DomainException("inventory.already_expired", $"The expiry date for {product.Label} is in the past.");

        var batch = new Batch(product.Id, number, expiry, Now);
        db.Batches.Add(batch);
        return batch;
    }

    /// <summary>
    /// First-expiry-first-out over non-expired batches (no-expiry batches last, then oldest first).
    /// Expired units are never picked automatically.
    /// </summary>
    public List<(BatchBalance Balance, int Quantity)> PickFefo(IEnumerable<BatchBalance> candidates, int quantity, string productLabel)
    {
        var withStock = candidates.Where(b => b.QuantityOnHand > 0).ToList();
        var usable = withStock.Where(b => !b.Batch!.IsExpiredOn(Today))
            .OrderBy(b => b.Batch!.ExpiryDate is null)
            .ThenBy(b => b.Batch!.ExpiryDate)
            .ThenBy(b => b.Batch!.CreatedAt)
            .ToList();

        var picks = new List<(BatchBalance, int)>();
        var remaining = quantity;
        foreach (var balance in usable)
        {
            if (remaining == 0) break;
            var take = Math.Min(balance.QuantityOnHand, remaining);
            picks.Add((balance, take));
            remaining -= take;
        }
        if (remaining == 0) return picks;

        var expiredUnits = withStock.Sum(b => b.QuantityOnHand) - usable.Sum(b => b.QuantityOnHand);
        throw expiredUnits > 0
            ? new DomainException("inventory.only_expired_stock",
                $"Only expired stock is left for part of {productLabel} ({expiredUnits} expired unit(s)). Write it off and receive fresh stock.")
            : new DomainException("inventory.insufficient_batch_stock",
                $"Not enough units of {productLabel} in this warehouse's batches.");
    }

    public StockMovement Record(Guid warehouseId, Guid productId, Guid batchId, MovementType type, int quantityChange,
        MovementReference referenceType, Guid referenceId, string? referenceNumber,
        AdjustmentReason? reason = null, string? notes = null)
    {
        var movement = new StockMovement(warehouseId, productId, batchId, type, quantityChange, referenceType, referenceId,
            referenceNumber, currentUser.UserId, Now, reason, notes);
        db.StockMovements.Add(movement);
        return movement;
    }
}
