namespace AlaraERP.Application.Sales;

/// <summary>
/// Erases the append-only inventory and order-history rows for one test order.
/// The database implementation exposes only this narrow operation; the runtime role never receives
/// general DELETE permission on either ledger table.
/// </summary>
public interface IOrderDeletionLedger
{
    Task EraseAsync(Guid orderId, CancellationToken ct);
}
