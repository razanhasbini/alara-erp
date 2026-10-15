using AlaraERP.Domain.Inventory;

namespace AlaraERP.Application.Inventory;

/// <summary>
/// Row-locks stock balances (<c>SELECT … FOR UPDATE</c>) in a deterministic order so concurrent
/// operations on the same products serialise instead of overselling or deadlocking.
/// Holding the lock for (warehouse, product) is also the rule for changing that product's batch balances there.
/// </summary>
public interface IStockLocks
{
    /// <summary>
    /// Locks and returns tracked, freshly-read balances. With <paramref name="createMissing"/>, absent rows are
    /// created at zero first (receipts, transfers in); otherwise absent rows are simply not returned.
    /// Must be called inside a transaction.
    /// </summary>
    Task<IReadOnlyDictionary<(Guid WarehouseId, Guid ProductId), StockBalance>> LockAsync(
        IEnumerable<(Guid WarehouseId, Guid ProductId)> keys, bool createMissing, CancellationToken ct);
}
