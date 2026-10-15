using AlaraERP.Application.Inventory;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Infrastructure.Persistence;

/// <inheritdoc cref="IStockLocks"/>
internal sealed class StockLocks(AlaraDbContext db) : IStockLocks
{
    public async Task<IReadOnlyDictionary<(Guid WarehouseId, Guid ProductId), StockBalance>> LockAsync(
        IEnumerable<(Guid WarehouseId, Guid ProductId)> keys, bool createMissing, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock rows can only be locked inside a transaction.");

        var list = keys.Distinct().ToList();
        if (list.Count == 0) return new Dictionary<(Guid, Guid), StockBalance>();
        var warehouses = list.Select(k => k.WarehouseId).ToArray();
        var products = list.Select(k => k.ProductId).ToArray();

        if (createMissing)
        {
            await db.Database.ExecuteSqlAsync($"""
                INSERT INTO inventory.stock_balances (warehouse_id, product_id, quantity_on_hand, quantity_reserved, updated_at)
                SELECT k.w, k.p, 0, 0, now() FROM unnest({warehouses}, {products}) AS k(w, p)
                ON CONFLICT DO NOTHING
                """, ct);
        }

        // Identity resolution would hand back stale instances; drop them so the locked read is authoritative.
        var wanted = list.ToHashSet();
        foreach (var entry in db.ChangeTracker.Entries<StockBalance>()
                     .Where(e => e.State == EntityState.Unchanged && wanted.Contains((e.Entity.WarehouseId, e.Entity.ProductId)))
                     .ToList())
            entry.State = EntityState.Detached;

        // ORDER BY + FOR UPDATE locks rows in a fixed order, so concurrent operations cannot deadlock each other.
        var rows = await db.StockBalances
            .FromSql($"""
                SELECT * FROM inventory.stock_balances
                WHERE (warehouse_id, product_id) IN (SELECT * FROM unnest({warehouses}, {products}))
                ORDER BY warehouse_id, product_id
                FOR UPDATE
                """)
            .ToListAsync(ct);
        return rows.ToDictionary(r => (r.WarehouseId, r.ProductId));
    }
}
