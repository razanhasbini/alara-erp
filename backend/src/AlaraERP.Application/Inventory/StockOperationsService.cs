using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
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

        // Costs are admin-only: other staff record units, never costs. Their deliveries use the product's saved cost;
        // if there is none yet, the stock waits uncosted and is valued at the saved cost once the admin sets it.
        if (currentUser.HasPermission(Permissions.Costs.View))
        {
            // The admin must give every unit a cost: the cost typed for this delivery, otherwise the saved cost.
            var uncosted = request.Lines.Where(l => l.UnitCost is null && products[l.ProductId].StandardCost is null)
                .Select(l => products[l.ProductId].Label).Distinct().ToList();
            if (uncosted.Count > 0)
                throw new DomainException("inventory.cost_required",
                    $"{string.Join(", ", uncosted)} {(uncosted.Count == 1 ? "has" : "have")} no saved cost. Enter the unit cost for this delivery.");
        }
        else if (request.Lines.Any(l => l.UnitCost is not null))
        {
            throw new ForbiddenOperationException("inventory.cost_admin_only", "Only the admin can enter costs.");
        }

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
            // No cost typed for this delivery → the product's saved cost. A typed cost applies to this delivery only;
            // the product's saved cost is never changed here.
            var unitCost = line.UnitCost ?? product.StandardCost;
            receipt.AddLine(product.Id, batch.Id, line.Quantity, unitCost);
            batch.ApplyReceivedCost(await _ledger.UnitsOnHandAsync(batch.Id, ct), line.Quantity, unitCost);

            _ledger.GetOrCreateBatchBalance(batchBalances, warehouseId, batch).Add(line.Quantity);
            balances[(warehouseId, product.Id)].Add(line.Quantity);
            _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.Receipt, line.Quantity,
                MovementReference.Receipt, receipt.Id, number, StockLedger.CostOf(batch, product));
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
            batch.ApplyReceivedCost(await _ledger.UnitsOnHandAsync(batch.Id, ct), quantity, request.UnitCost ?? (batch.UnitCost is null ? product.StandardCost : null));
            _ledger.GetOrCreateBatchBalance(batchBalances, warehouseId, batch).Add(quantity);
            balances[(warehouseId, product.Id)].Add(quantity);
            movement = _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.AdjustmentIncrease, quantity,
                MovementReference.Adjustment, adjustmentId, "ADJ", StockLedger.CostOf(batch, product), request.Reason, request.Notes);
        }
        else
        {
            if (!balances.TryGetValue((warehouseId, product.Id), out var balance)
                || !batchBalances.TryGetValue((warehouseId, batch.Id), out var batchBalance))
                throw new DomainException("inventory.insufficient_batch_stock", $"Batch {batch.BatchNumber} has no stock in this warehouse.");

            batchBalance.Remove(quantity, batch.BatchNumber);
            balance.RemoveUnreserved(quantity, product.Label);
            movement = _ledger.Record(warehouseId, product.Id, batch.Id, MovementType.AdjustmentDecrease, -quantity,
                MovementReference.Adjustment, adjustmentId, "ADJ", StockLedger.CostOf(batch, product), request.Reason, request.Notes);
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
                picks = await _ledger.PickAsync(batchBalances.Values.Where(b => b.WarehouseId == from.Id && b.Batch!.ProductId == product.Id),
                    line.Quantity, product.Label, ct);
            }

            foreach (var (source, take) in picks)
            {
                var batch = source.Batch!;
                source.Remove(take, batch.BatchNumber);
                _ledger.GetOrCreateBatchBalance(batchBalances, to.Id, batch).Add(take);
                balances[(to.Id, product.Id)].Add(take);
                transfer.AddLine(product.Id, batch.Id, take);
                var cost = StockLedger.CostOf(batch, product);
                _ledger.Record(from.Id, product.Id, batch.Id, MovementType.TransferOut, -take, MovementReference.Transfer, transfer.Id, number, cost);
                _ledger.Record(to.Id, product.Id, batch.Id, MovementType.TransferIn, take, MovementReference.Transfer, transfer.Id, number, cost);
            }
        }

        _db.StockTransfers.Add(transfer);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await _queries.GetTransferAsync(transfer.Id, ct);
    }

    /// <summary>
    /// Takes complete bundles out of stock without an order (PR boxes, gifts, damaged sets): each component is
    /// deducted (earliest expiry first) and logged as an adjustment that names the bundle. Bundles have no stock of their own.
    /// </summary>
    public async Task<IReadOnlyList<MovementDto>> WithdrawBundleAsync(WithdrawBundleRequest request, CancellationToken ct)
    {
        var warehouseId = request.WarehouseId;
        await _ledger.RequireActiveWarehouseAsync(warehouseId, ct);
        var bundle = await _db.Products.AsNoTracking().Include(p => p.BundleItems)
                         .FirstOrDefaultAsync(p => p.Id == request.BundleProductId, ct)
                     ?? throw new NotFoundException("Bundle", request.BundleProductId);
        if (bundle.Type != Domain.Catalog.ProductType.Bundle || bundle.BundleItems.Count == 0)
            throw new DomainException("bundles.not_a_bundle", $"'{bundle.Name}' is not a bundle with components.");

        var needs = bundle.BundleItems.ToDictionary(b => b.ComponentProductId, b => b.Quantity * request.Quantity);
        var products = await _ledger.LoadStockableProductsAsync(needs.Keys, ct);
        var adjustmentId = Guid.CreateVersion7();
        var notes = $"{request.Quantity} × {bundle.Name}" + (string.IsNullOrWhiteSpace(request.Notes) ? "" : $" — {request.Notes.Trim()}");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var balances = await _locks.LockAsync(needs.Keys.Select(p => (warehouseId, p)), createMissing: false, ct);
        var batchBalances = await _ledger.LoadBatchBalancesAsync([warehouseId], needs.Keys, ct);

        foreach (var (productId, quantity) in needs.OrderBy(n => n.Key))
        {
            var product = products[productId];
            if (!balances.TryGetValue((warehouseId, productId), out var balance))
                throw new DomainException("inventory.insufficient_available", $"There is no {product.Label} in this warehouse.");
            balance.RemoveUnreserved(quantity, product.Label);
            var candidates = batchBalances.Values.Where(b => b.Batch!.ProductId == productId);
            foreach (var (batchBalance, take) in await _ledger.PickAsync(candidates, quantity, product.Label, ct))
            {
                batchBalance.Remove(take, batchBalance.Batch!.BatchNumber);
                _ledger.Record(warehouseId, productId, batchBalance.BatchId, MovementType.AdjustmentDecrease, -take,
                    MovementReference.Adjustment, adjustmentId, "BUNDLE", StockLedger.CostOf(batchBalance.Batch, product), request.Reason, notes);
            }
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await _queries.ListMovementsAsync(new MovementQuery(null, null, null, null, null, adjustmentId, 1, 100), ct)).Items;
    }
}
