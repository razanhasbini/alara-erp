using AlaraERP.Application.Common;
using AlaraERP.Application.Inventory.Contracts;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

internal sealed class StockReservationService(IAlaraDbContext db, IStockLocks locks, StockLedger ledger) : IStockReservationService
{
    public async Task ReserveAsync(Guid orderId, Guid warehouseId, IReadOnlyDictionary<Guid, int> quantities, CancellationToken ct)
    {
        EnsureTransaction();
        await ledger.RequireActiveWarehouseAsync(warehouseId, ct);
        if (await db.StockReservations.AnyAsync(r => r.OrderId == orderId && r.Status == ReservationStatus.Active, ct))
            throw new InvalidOperationException($"Order {orderId} already has active reservations.");

        var products = await ledger.LoadStockableProductsAsync(quantities.Keys, ct);
        var balances = await locks.LockAsync(quantities.Keys.Select(p => (warehouseId, p)), createMissing: false, ct);

        foreach (var (productId, quantity) in quantities.OrderBy(q => q.Key))
        {
            var label = products[productId].Label;
            if (!balances.TryGetValue((warehouseId, productId), out var balance))
                throw new DomainException("inventory.insufficient_stock", $"Not enough stock of {label}: {quantity} needed, 0 available.");
            balance.Reserve(quantity, label);
            db.StockReservations.Add(new StockReservation(orderId, productId, warehouseId, quantity, ledger.Now));
        }
    }

    public async Task ReleaseAsync(Guid orderId, CancellationToken ct)
    {
        EnsureTransaction();
        var reservations = await ActiveReservationsAsync(orderId, ct);
        if (reservations.Count == 0) return;

        var balances = await locks.LockAsync(reservations.Select(r => (r.WarehouseId, r.ProductId)), createMissing: false, ct);
        foreach (var reservation in reservations)
        {
            balances[(reservation.WarehouseId, reservation.ProductId)].Release(reservation.Quantity);
            reservation.Close(ReservationStatus.Released, ledger.Now);
        }
    }

    public async Task ShipAsync(Guid orderId, string orderNumber, CancellationToken ct)
    {
        EnsureTransaction();
        var reservations = await ActiveReservationsAsync(orderId, ct);
        // Shipping is now a separate logistics action. Orders confirmed under the current workflow
        // were already fulfilled; legacy active reservations are still handled here once.
        if (reservations.Count == 0) return;

        var products = await ledger.LoadStockableProductsAsync(reservations.Select(r => r.ProductId), ct);
        var balances = await locks.LockAsync(reservations.Select(r => (r.WarehouseId, r.ProductId)), createMissing: false, ct);
        var batchBalances = await ledger.LoadBatchBalancesAsync(
            reservations.Select(r => r.WarehouseId), reservations.Select(r => r.ProductId), ct);

        foreach (var reservation in reservations.OrderBy(r => r.ProductId))
        {
            var candidates = batchBalances.Values.Where(b =>
                b.WarehouseId == reservation.WarehouseId && b.Batch!.ProductId == reservation.ProductId);
            var label = products[reservation.ProductId].Label;

            foreach (var (batchBalance, take) in await ledger.PickAsync(candidates, reservation.Quantity, label, ct))
            {
                batchBalance.Remove(take, batchBalance.Batch!.BatchNumber);
                ledger.Record(reservation.WarehouseId, reservation.ProductId, batchBalance.BatchId, MovementType.Shipment, -take,
                    MovementReference.Order, orderId, orderNumber, StockLedger.CostOf(batchBalance.Batch, products[reservation.ProductId]));
            }
            balances[(reservation.WarehouseId, reservation.ProductId)].FulfilReserved(reservation.Quantity);
            reservation.Close(ReservationStatus.Fulfilled, ledger.Now);
        }
    }

    public async Task ReturnShipmentAsync(Guid orderId, string orderNumber, CancellationToken ct)
    {
        EnsureTransaction();
        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ReferenceType == MovementReference.Order && m.ReferenceId == orderId)
            .ToListAsync(ct);
        var shipped = movements.Where(m => m.Type is MovementType.Shipment or MovementType.Return)
            .GroupBy(m => (m.WarehouseId, m.ProductId, m.BatchId))
            .Select(g => (g.Key.WarehouseId, g.Key.ProductId, g.Key.BatchId, Quantity: -g.Sum(m => m.QuantityChange),
                UnitCost: g.Where(m => m.Type == MovementType.Shipment).Select(m => m.UnitCost).FirstOrDefault()))
            .Where(x => x.Quantity > 0)
            .ToList();
        if (shipped.Count == 0) return;

        var balances = await locks.LockAsync(shipped.Select(s => (s.WarehouseId, s.ProductId)), createMissing: true, ct);
        var batchBalances = await ledger.LoadBatchBalancesAsync(shipped.Select(s => s.WarehouseId), shipped.Select(s => s.ProductId), ct);
        var batchIds = shipped.Select(s => s.BatchId).ToList();
        var batches = await db.Batches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);

        foreach (var line in shipped)
        {
            ledger.GetOrCreateBatchBalance(batchBalances, line.WarehouseId, batches[line.BatchId]).Add(line.Quantity);
            balances[(line.WarehouseId, line.ProductId)].Add(line.Quantity);
            // Returned units come back at the cost they left with, so the order's net cost of goods is zero.
            ledger.Record(line.WarehouseId, line.ProductId, line.BatchId, MovementType.Return, line.Quantity,
                MovementReference.Order, orderId, orderNumber, line.UnitCost);
        }
    }

    private Task<List<StockReservation>> ActiveReservationsAsync(Guid orderId, CancellationToken ct) =>
        db.StockReservations.Where(r => r.OrderId == orderId && r.Status == ReservationStatus.Active).ToListAsync(ct);

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Stock reservation operations must run inside a database transaction.");
    }
}
