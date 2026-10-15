using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>Receipts, adjustments and transfers. Each runs in one transaction with stock rows locked.</summary>
public sealed class StockOperationsService(
    IAlaraDbContext db, IStockLocks locks, StockLedger ledger, IDocumentNumbers numbers,
    StockQueryService queries, ICurrentUser currentUser)
{
    private readonly IAlaraDbContext _db = db;
    private readonly IStockLocks _locks = locks;
    private readonly StockLedger _ledger = ledger;
    private readonly IDocumentNumbers _numbers = numbers;
    private readonly StockQueryService _queries = queries;

    private Guid? CurrentUserId => currentUser.UserId;

    public async Task<ReceiptDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken ct)
    {
        var warehouseId = request.WarehouseId;
        await _ledger.RequireActiveWarehouseAsync(warehouseId, ct);
        var products = await _ledger.LoadStockableProductsAsync(request.Lines.Select(l => l.ProductId), ct);

        var duplicate = request.Lines
            .GroupBy(l => (l.ProductId, Batch: l.BatchNumber?.Trim().ToUpperInvariant() ?? ""))
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new DomainException("inventory.duplicate_receipt_line",
                $"{products[duplicate.Key.ProductId].Label} is listed twice for the same batch. Combine the lines or enter distinct batch numbers.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var number = await _numbers.NextReceiptNumberAsync(ct);
        var receipt = new StockReceipt(number, warehouseId, request.SupplierName, request.SupplierReference,
            request.ReceivedAt ?? _ledger.Now, request.Notes, CurrentUserId, _ledger.Now);

        var balances = await _locks.LockAsync(products.Keys.Select(p => (warehouseId, p)), createMissing: true, ct);
        var batchBalances = await _ledger.LoadBatchBalancesAsync([warehouseId], products.Keys, ct);

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            var batch = await _ledger.ResolveIncomingBatchAsync(product, line.BatchNumber, line.ExpiryDate, number, ct);
            receipt.AddLine(product.Id, batch.Id, line.Quantity, line.UnitCost);

            _ledger.GetOrCreateBatchBalance(batchBalances, warehouseId, batch).Add(line.Quantity);
            balances[(warehouseId, product.Id)].Add(line.Quantity);
            _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.Receipt, line.Quantity,
                MovementReference.Receipt, receipt.Id, number);
        }

        _db.StockReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await _queries.GetReceiptAsync(receipt.Id, ct);
    }

    public async Task<MovementDto> AdjustAsync(AdjustStockRequest request, CancellationToken ct)
    {
        var warehouseId = request.WarehouseId;
        await _ledger.RequireActiveWarehouseAsync(warehouseId, ct);
        var product = (await _ledger.LoadStockableProductsAsync([request.ProductId], ct))[request.ProductId];
        var adjustmentId = Guid.CreateVersion7();
        var increase = request.QuantityChange > 0;
        var quantity = Math.Abs(request.QuantityChange);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var balances = await _locks.LockAsync([(warehouseId, product.Id)], createMissing: increase, ct);
        var batchBalances = await _ledger.LoadBatchBalancesAsync([warehouseId], [product.Id], ct);

        Batch batch;
        if (request.BatchId is { } batchId)
        {
            batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == batchId, ct) ?? throw new NotFoundException("Batch", batchId);
            if (batch.ProductId != product.Id)
                throw new DomainException("inventory.batch_product_mismatch", "That batch belongs to a different product.");
        }
        else
        {
            batch = await _ledger.ResolveIncomingBatchAsync(product, request.BatchNumber, request.ExpiryDate,
                $"ADJ-{_ledger.Today:yyyyMMdd}", ct);
        }

        StockMovement movement;
        if (increase)
        {
            _ledger.GetOrCreateBatchBalance(batchBalances, warehouseId, batch).Add(quantity);
            balances[(warehouseId, product.Id)].Add(quantity);
            movement = _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.AdjustmentIncrease, quantity,
                MovementReference.Adjustment, adjustmentId, "ADJ", request.Reason, request.Notes);
        }
        else
        {
            if (!balances.TryGetValue((warehouseId, product.Id), out var balance)
                || !batchBalances.TryGetValue((warehouseId, batch.Id), out var batchBalance))
                throw new DomainException("inventory.insufficient_batch_stock", $"Batch {batch.BatchNumber} has no stock in this warehouse.");

            batchBalance.Remove(quantity, batch.BatchNumber);
            balance.RemoveUnreserved(quantity, product.Label);
            movement = _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.AdjustmentDecrease, -quantity,
                MovementReference.Adjustment, adjustmentId, "ADJ", request.Reason, request.Notes);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await _queries.ListMovementsAsync(new MovementQuery(null, null, null, null, null, adjustmentId), ct)).Items.Single();
    }

    public async Task<TransferDto> TransferAsync(TransferStockRequest request, CancellationToken ct)
    {
        var from = await _ledger.RequireActiveWarehouseAsync(request.FromWarehouseId, ct);
        var to = await _ledger.RequireActiveWarehouseAsync(request.ToWarehouseId, ct);
        var products = await _ledger.LoadStockableProductsAsync(request.Lines.Select(l => l.ProductId), ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var number = await _numbers.NextTransferNumberAsync(ct);
        var transfer = new StockTransfer(number, from.Id, to.Id, request.Notes, CurrentUserId, _ledger.Now);

        var keys = products.Keys.SelectMany(p => new[] { (from.Id, p), (to.Id, p) });
        var balances = await _locks.LockAsync(keys, createMissing: true, ct);
        var batchBalances = await _ledger.LoadBatchBalancesAsync([from.Id, to.Id], products.Keys, ct);

        foreach (var line in request.Lines)
        {
            var product = products[line.ProductId];
            balances[(from.Id, product.Id)].RemoveUnreserved(line.Quantity, product.Label);

            List<(BatchBalance Balance, int Quantity)> picks;
            if (line.BatchId is { } batchId)
            {
                if (!batchBalances.TryGetValue((from.Id, batchId), out var chosen) || chosen.Batch!.ProductId != product.Id)
                    throw new DomainException("inventory.insufficient_batch_stock", $"That batch of {product.Label} has no stock in {from.Code}.");
                picks = [(chosen, line.Quantity)];
            }
            else
            {
                picks = _ledger.PickFefo(batchBalances.Values.Where(b => b.WarehouseId == from.Id && b.Batch!.ProductId == product.Id),
                    line.Quantity, product.Label);
            }

            foreach (var (source, take) in picks)
            {
                var batch = source.Batch!;
                source.Remove(take, batch.BatchNumber);
                _ledger.GetOrCreateBatchBalance(batchBalances, to.Id, batch).Add(take);
                balances[(to.Id, product.Id)].Add(take);
                transfer.AddLine(product.Id, batch.Id, take);
                _ledger.Record(from.Id, product.Id, batch.Id, MovementType.TransferOut, -take, MovementReference.Transfer, transfer.Id, number);
                _ledger.Record(to.Id, product.Id, batch.Id, MovementType.TransferIn, take, MovementReference.Transfer, transfer.Id, number);
            }
        }

        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await _queries.GetTransferAsync(transfer.Id, ct);
    }
}
