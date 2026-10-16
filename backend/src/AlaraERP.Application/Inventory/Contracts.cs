using AlaraERP.Domain.Common;

namespace AlaraERP.Application.Inventory.Contracts;

/// <summary>Not enough unreserved stock of one product. Carries the numbers so callers can explain who holds the rest.</summary>
public sealed class InsufficientStockException(Guid productId, string productName, int needed, int available)
    : DomainException("inventory.insufficient_stock", $"Not enough stock of {productName}: {needed} needed, {Math.Max(0, available)} available.")
{
    public Guid ProductId { get; } = productId;
    public string ProductName { get; } = productName;
    public int Needed { get; } = needed;
    public int Available { get; } = Math.Max(0, available);
}

/// <summary>
/// The Inventory module's public interface for order fulfilment. Sales calls these — and only these —
/// to affect stock. All methods must run inside the caller's database transaction; they stage changes
/// on the shared unit of work and the caller commits.
/// </summary>
public interface IStockReservationService
{
    /// <summary>Stages the quantities needed by an order, or throws if any product is short.</summary>
    Task ReserveAsync(Guid orderId, Guid warehouseId, IReadOnlyDictionary<Guid, int> quantities, CancellationToken ct);

    /// <summary>Releases the order's active reservations (cancel / reopen). No-op if there are none.</summary>
    Task ReleaseAsync(Guid orderId, CancellationToken ct);

    /// <summary>Deducts staged stock from non-expired batches (FEFO). Called during confirmation; a later shipping call is a compatibility no-op.</summary>
    Task ShipAsync(Guid orderId, string orderNumber, CancellationToken ct);

    /// <summary>Puts shipped units back into the batches they left from (courier return).</summary>
    Task ReturnShipmentAsync(Guid orderId, string orderNumber, CancellationToken ct);
}

public sealed record WarehouseInfo(Guid Id, string Name, string Code, bool IsActive);

public interface IWarehouseDirectory
{
    Task<WarehouseInfo?> FindAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyDictionary<Guid, WarehouseInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct);
}
