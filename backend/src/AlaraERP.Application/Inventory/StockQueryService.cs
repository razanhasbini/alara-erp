using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Inventory;

public sealed class StockQueryService(IAlaraDbContext db, BusinessClock clock, IUserDirectory users, IOptions<ErpOptions> options,
    ICurrentUser currentUser, InventoryFeatureService features)
{
    private int WarningDays => options.Value.ExpiryWarningDays;

    public async Task<PagedResult<StockRowDto>> ListStockAsync(StockListQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var today = clock.Today;
        var warnUntil = today.AddDays(WarningDays);
        var warehouseId = query.WarehouseId;
        var expiryEnabled = await features.ExpiryDatesEnabledAsync(ct);

        var products = db.Products.AsNoTracking().Where(p => p.Type == ProductType.Individual);
        if (!query.IncludeInactive) products = products.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Text.ContainsPattern(query.Search);
            products = products.Where(p => EF.Functions.Like(p.Name.ToLower(), term, "\\"));
        }
        if (query.CategoryId is { } categoryId)
            products = products.Where(p => p.CategoryId == categoryId
                                        || db.Categories.Any(c => c.Id == p.CategoryId && c.ParentCategoryId == categoryId));

        var rows = products.Select(p => new
        {
            p.Id, p.Name, p.IsActive,
            CategoryName = db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
            OnHand = db.StockBalances.Where(b => b.ProductId == p.Id && (warehouseId == null || b.WarehouseId == warehouseId))
                .Sum(b => (int?)b.QuantityOnHand) ?? 0,
            Reserved = db.StockBalances.Where(b => b.ProductId == p.Id && (warehouseId == null || b.WarehouseId == warehouseId))
                .Sum(b => (int?)b.QuantityReserved) ?? 0,
            Threshold = db.ProductInventorySettings.Where(s => s.ProductId == p.Id).Select(s => (int?)s.LowStockThreshold).FirstOrDefault()
                        ?? InventorySettingsService.DefaultThreshold,
            NearestExpiry = expiryEnabled ? db.BatchBalances
                .Where(bb => bb.QuantityOnHand > 0 && bb.Batch!.ProductId == p.Id && (warehouseId == null || bb.WarehouseId == warehouseId)
                             && bb.Batch.ExpiryDate >= today)
                .Min(bb => bb.Batch!.ExpiryDate) : null,
            ExpiringUnits = expiryEnabled ? db.BatchBalances
                .Where(bb => bb.Batch!.ProductId == p.Id && (warehouseId == null || bb.WarehouseId == warehouseId)
                             && bb.Batch.ExpiryDate >= today && bb.Batch.ExpiryDate <= warnUntil)
                .Sum(bb => (int?)bb.QuantityOnHand) ?? 0 : 0,
            ExpiredUnits = expiryEnabled ? db.BatchBalances
                .Where(bb => bb.Batch!.ProductId == p.Id && (warehouseId == null || bb.WarehouseId == warehouseId)
                             && bb.Batch.ExpiryDate < today)
                .Sum(bb => (int?)bb.QuantityOnHand) ?? 0 : 0,
        });

        rows = query.Filter switch
        {
            StockFilter.Out => rows.Where(r => r.OnHand - r.Reserved <= 0),
            StockFilter.Low => rows.Where(r => r.OnHand - r.Reserved > 0 && r.OnHand - r.Reserved <= r.Threshold),
            StockFilter.Expiring when expiryEnabled => rows.Where(r => r.ExpiringUnits > 0),
            StockFilter.Expired when expiryEnabled => rows.Where(r => r.ExpiredUnits > 0),
            StockFilter.Expiring or StockFilter.Expired => rows.Where(_ => false),
            _ => rows,
        };
        rows = query.Sort switch
        {
            "available" => rows.OrderBy(r => r.OnHand - r.Reserved).ThenBy(r => r.Name),
            "-available" => rows.OrderByDescending(r => r.OnHand - r.Reserved).ThenBy(r => r.Name),
            "expiry" => rows.OrderBy(r => r.NearestExpiry == null).ThenBy(r => r.NearestExpiry).ThenBy(r => r.Name),
            "-name" => rows.OrderByDescending(r => r.Name),
            _ => rows.OrderBy(r => r.Name),
        };

        var total = await rows.CountAsync(ct);
        var items = await rows.Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        return new PagedResult<StockRowDto>(
            items.Select(r => new StockRowDto(r.Id, r.Name, r.CategoryName, r.IsActive, r.OnHand, r.Reserved,
                r.OnHand - r.Reserved, r.Threshold, StatusOf(r.OnHand - r.Reserved, r.Threshold),
                r.NearestExpiry, r.ExpiringUnits, r.ExpiredUnits)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<ProductStockDto> GetProductStockAsync(Guid productId, CancellationToken ct)
    {
        var product = await db.Products.AsNoTracking().Where(p => p.Id == productId)
                          .Select(p => new { p.Id, p.Name, p.Type, p.StandardCost }).FirstOrDefaultAsync(ct)
                      ?? throw new NotFoundException("Product", productId);
        if (product.Type == ProductType.Bundle)
            throw new Domain.Common.DomainException("inventory.bundle_not_stocked",
                "Bundles have no stock of their own; see their components.");

        var settings = InventorySettingsService.ToDto(productId,
            await db.ProductInventorySettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == productId, ct));

        var warehouses = await (from b in db.StockBalances.AsNoTracking()
                                join w in db.Warehouses on b.WarehouseId equals w.Id
                                where b.ProductId == productId
                                orderby w.Code
                                select new WarehouseStockDto(w.Id, w.Name, w.Code, b.QuantityOnHand, b.QuantityReserved,
                                    b.QuantityOnHand - b.QuantityReserved))
            .ToListAsync(ct);

        var today = clock.Today;
        var expiryEnabled = await features.ExpiryDatesEnabledAsync(ct);
        var batches = await (from bb in db.BatchBalances.AsNoTracking()
                             join w in db.Warehouses on bb.WarehouseId equals w.Id
                             where bb.Batch!.ProductId == productId && bb.QuantityOnHand > 0
                             select new { bb.BatchId, bb.Batch!.BatchNumber, bb.Batch.ExpiryDate, bb.Batch.UnitCost, w.Id, w.Code, bb.QuantityOnHand })
            .ToListAsync(ct);

        var onHand = warehouses.Sum(w => w.OnHand);
        var reserved = warehouses.Sum(w => w.Reserved);
        var canSeeCosts = currentUser.HasPermission(Permissions.Costs.View);

        ProductCostDto? cost = null;
        if (canSeeCosts)
        {
            var lastReceipt = await (from l in db.StockReceipts.SelectMany(r => r.Lines, (r, l) => new { r.ReceivedAt, l.ProductId, l.UnitCost })
                                     where l.ProductId == productId && l.UnitCost != null
                                     orderby l.ReceivedAt descending
                                     select new { l.UnitCost, l.ReceivedAt }).FirstOrDefaultAsync(ct);
            var costed = batches.Select(b => (b.QuantityOnHand, Cost: b.UnitCost ?? product.StandardCost)).ToList();
            var costedUnits = costed.Where(c => c.Cost is not null).Sum(c => c.QuantityOnHand);
            var value = costed.Where(c => c.Cost is not null).Sum(c => c.QuantityOnHand * c.Cost!.Value);
            var uncostedUnits = costed.Where(c => c.Cost is null).Sum(c => c.QuantityOnHand);
            var fullyCosted = uncostedUnits == 0;
            cost = new ProductCostDto(product.StandardCost,
                costedUnits > 0 && fullyCosted ? Math.Round(value / costedUnits, 4) : batches.Count == 0 ? product.StandardCost : null,
                lastReceipt?.UnitCost, lastReceipt?.ReceivedAt,
                costedUnits > 0 && fullyCosted ? Math.Round(value, 2) : null,
                uncostedUnits);
        }

        return new ProductStockDto(product.Id, product.Name, settings, onHand, reserved, onHand - reserved,
            StatusOf(onHand - reserved, settings.LowStockThreshold),
            warehouses,
            batches.OrderBy(b => b.ExpiryDate is null).ThenBy(b => b.ExpiryDate).ThenBy(b => b.Code)
                .Select(b => new BatchStockDto(b.BatchId, b.BatchNumber, expiryEnabled ? b.ExpiryDate : null, b.Id, b.Code, b.QuantityOnHand,
                    expiryEnabled && b.ExpiryDate is { } e && e < today,
                    expiryEnabled && b.ExpiryDate is { } d ? d.DayNumber - today.DayNumber : null,
                    canSeeCosts ? b.UnitCost : null))
                .ToList(),
            cost);
    }

    /// <summary>
    /// Sellable units now. Individuals: on hand − reserved. Bundles: how many complete sets the components allow.
    /// With no warehouse given, individuals are summed across warehouses, while bundles use the best single
    /// warehouse (an order ships from one warehouse).
    /// </summary>
    public async Task<IReadOnlyList<AvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<Guid> productIds, Guid? warehouseId, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var bundleItems = await db.BundleItems.AsNoTracking().Where(b => ids.Contains(b.BundleProductId))
            .Select(b => new { b.BundleProductId, b.ComponentProductId, b.Quantity }).ToListAsync(ct);
        var stockIds = ids.Concat(bundleItems.Select(b => b.ComponentProductId)).Distinct().ToList();

        var balances = await db.StockBalances.AsNoTracking()
            .Where(b => stockIds.Contains(b.ProductId) && (warehouseId == null || b.WarehouseId == warehouseId))
            .Select(b => new { b.WarehouseId, b.ProductId, Available = b.QuantityOnHand - b.QuantityReserved })
            .ToListAsync(ct);
        var warehouses = balances.Select(b => b.WarehouseId).Distinct().ToList();
        int AvailableIn(Guid warehouse, Guid product) =>
            balances.FirstOrDefault(b => b.WarehouseId == warehouse && b.ProductId == product)?.Available ?? 0;

        var bundles = bundleItems.ToLookup(b => b.BundleProductId);
        return ids.Select(id =>
        {
            if (!bundles[id].Any())
                return new AvailabilityDto(id, balances.Where(b => b.ProductId == id).Sum(b => b.Available));
            var best = warehouses.Count == 0 ? 0 : warehouses.Max(w =>
                bundles[id].Min(c => Math.Max(0, AvailableIn(w, c.ComponentProductId)) / c.Quantity));
            return new AvailabilityDto(id, best);
        }).ToList();
    }

    public async Task<PagedResult<MovementDto>> ListMovementsAsync(MovementQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = db.StockMovements.AsNoTracking();
        if (query.WarehouseId is { } warehouse) q = q.Where(m => m.WarehouseId == warehouse);
        if (query.ProductId is { } p) q = q.Where(m => m.ProductId == p);
        if (query.Type is { } t) q = q.Where(m => m.Type == t);
        if (query.From is { } from) q = q.Where(m => m.CreatedAt >= from);
        if (query.To is { } to) q = q.Where(m => m.CreatedAt < to);
        if (query.ReferenceId is { } r) q = q.Where(m => m.ReferenceId == r);

        var total = await q.CountAsync(ct);
        var rows = await (from m in q
                          join w in db.Warehouses on m.WarehouseId equals w.Id
                          join pr in db.Products on m.ProductId equals pr.Id
                          join b in db.Batches on m.BatchId equals b.Id
                          orderby m.CreatedAt descending, m.Id descending
                          select new { m, w.Code, pr.Name, b.BatchNumber, b.ExpiryDate })
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);

        var names = await users.GetDisplayNamesAsync(rows.Select(r => r.m.PerformedByUserId), ct);
        var withCosts = currentUser.HasPermission(Permissions.Costs.View);
        var expiryEnabled = await features.ExpiryDatesEnabledAsync(ct);
        return new PagedResult<MovementDto>(rows.Select(r => new MovementDto(
                r.m.Id, r.m.CreatedAt, r.m.WarehouseId, r.Code, r.m.ProductId, r.Name, r.BatchNumber, expiryEnabled ? r.ExpiryDate : null,
                r.m.Type, r.m.QuantityChange, r.m.Reason, r.m.ReferenceType, r.m.ReferenceId, r.m.ReferenceNumber, r.m.Notes,
                r.m.PerformedByUserId is { } u ? names.GetValueOrDefault(u) : null,
                withCosts ? r.m.UnitCost : null)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<PagedResult<ReceiptSummaryDto>> ListReceiptsAsync(Guid? warehouseId, string? search, int pageNumber, int pageSize, CancellationToken ct)
    {
        var page = new PageRequest { Page = pageNumber, PageSize = pageSize };
        var q = db.StockReceipts.AsNoTracking();
        if (warehouseId is { } w) q = q.Where(r => r.WarehouseId == w);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = Text.ContainsPattern(search);
            q = q.Where(r => EF.Functions.Like(r.ReceiptNumber.ToLower(), term, "\\")
                          || (r.SupplierName != null && EF.Functions.Like(r.SupplierName.ToLower(), term, "\\"))
                          || (r.SupplierReference != null && EF.Functions.Like(r.SupplierReference.ToLower(), term, "\\")));
        }
        var total = await q.CountAsync(ct);
        var rows = await (from r in q
                          join wh in db.Warehouses on r.WarehouseId equals wh.Id
                          orderby r.ReceivedAt descending, r.Id descending
                          select new { r, wh.Code, Lines = r.Lines.Count, Units = r.Lines.Sum(l => l.Quantity) })
            .Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(rows.Select(x => x.r.PerformedByUserId), ct);
        return new PagedResult<ReceiptSummaryDto>(rows.Select(x => new ReceiptSummaryDto(
                x.r.Id, x.r.ReceiptNumber, x.Code, x.r.SupplierName, x.r.SupplierReference, x.r.ReceivedAt, x.Lines, x.Units,
                x.r.PerformedByUserId is { } u ? names.GetValueOrDefault(u) : null)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken ct)
    {
        var receipt = await db.StockReceipts.AsNoTracking().Include(r => r.Lines).FirstOrDefaultAsync(r => r.Id == id, ct)
                      ?? throw new NotFoundException("Receipt", id);
        var warehouse = await db.Warehouses.AsNoTracking().Where(w => w.Id == receipt.WarehouseId).Select(w => w.Name).FirstAsync(ct);
        var lines = await LineDetailsAsync(receipt.Lines.Select(l => (l.ProductId, l.BatchId)), ct);
        var names = await users.GetDisplayNamesAsync([receipt.PerformedByUserId], ct);
        var canSeeCosts = currentUser.HasPermission(Permissions.Costs.View);

        var dtoLines = receipt.Lines.Select(l =>
        {
            var (name, batch, expiry) = lines[(l.ProductId, l.BatchId)];
            return new ReceiptLineDto(l.ProductId, name, l.BatchId, batch, expiry, l.Quantity, canSeeCosts ? l.UnitCost : null);
        }).OrderBy(l => l.ProductName).ThenBy(l => l.BatchNumber).ToList();
        var costed = canSeeCosts && receipt.Lines.All(l => l.UnitCost.HasValue);
        return new ReceiptDto(receipt.Id, receipt.ReceiptNumber, receipt.WarehouseId, warehouse, receipt.SupplierName,
            receipt.SupplierReference, receipt.ReceivedAt, receipt.Notes,
            receipt.PerformedByUserId is { } u ? names.GetValueOrDefault(u) : null, receipt.CreatedAt,
            receipt.Lines.Sum(l => l.Quantity),
            costed ? Math.Round(receipt.Lines.Sum(l => l.UnitCost!.Value * l.Quantity), 2) : null,
            dtoLines);
    }

    public async Task<PagedResult<TransferDto>> ListTransfersAsync(Guid? warehouseId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var page = new PageRequest { Page = pageNumber, PageSize = pageSize };
        var q = db.StockTransfers.AsNoTracking();
        if (warehouseId is { } w) q = q.Where(t => t.FromWarehouseId == w || t.ToWarehouseId == w);
        var total = await q.CountAsync(ct);
        var ids = await q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
            .Skip(page.Skip).Take(page.SafePageSize).Select(t => t.Id).ToListAsync(ct);
        var items = new List<TransferDto>();
        foreach (var id in ids) items.Add(await GetTransferAsync(id, ct));
        return new PagedResult<TransferDto>(items, page.SafePage, page.SafePageSize, total);
    }

    public async Task<TransferDto> GetTransferAsync(Guid id, CancellationToken ct)
    {
        var transfer = await db.StockTransfers.AsNoTracking().Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, ct)
                       ?? throw new NotFoundException("Transfer", id);
        var warehouses = await db.Warehouses.AsNoTracking()
            .Where(w => w.Id == transfer.FromWarehouseId || w.Id == transfer.ToWarehouseId)
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var lines = await LineDetailsAsync(transfer.Lines.Select(l => (l.ProductId, l.BatchId)), ct);
        var names = await users.GetDisplayNamesAsync([transfer.PerformedByUserId], ct);

        return new TransferDto(transfer.Id, transfer.TransferNumber,
            transfer.FromWarehouseId, warehouses[transfer.FromWarehouseId], transfer.ToWarehouseId, warehouses[transfer.ToWarehouseId],
            transfer.Status, transfer.Notes, transfer.PerformedByUserId is { } u ? names.GetValueOrDefault(u) : null,
            transfer.CreatedAt, transfer.Lines.Sum(l => l.Quantity),
            transfer.Lines.Select(l =>
                {
                    var (name, batch, expiry) = lines[(l.ProductId, l.BatchId)];
                    return new TransferLineDto(l.ProductId, name, l.BatchId, batch, expiry, l.Quantity);
                })
                .OrderBy(l => l.ProductName).ThenBy(l => l.ExpiryDate is null).ThenBy(l => l.ExpiryDate).ThenBy(l => l.BatchNumber)
                .ToList());
    }

    internal static StockStatus StatusOf(int available, int threshold) =>
        available <= 0 ? StockStatus.Out : available <= threshold ? StockStatus.Low : StockStatus.InStock;

    private async Task<Dictionary<(Guid, Guid), (string Name, string Batch, DateOnly? Expiry)>> LineDetailsAsync(
        IEnumerable<(Guid ProductId, Guid BatchId)> keys, CancellationToken ct)
    {
        var list = keys.Distinct().ToList();
        var productIds = list.Select(k => k.ProductId).ToList();
        var batchIds = list.Select(k => k.BatchId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Name, ct);
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id, b => (b.BatchNumber, b.ExpiryDate), ct);
        var expiryEnabled = await features.ExpiryDatesEnabledAsync(ct);
        return list.ToDictionary(k => k, k => (products[k.ProductId],
            batches[k.BatchId].BatchNumber, expiryEnabled ? batches[k.BatchId].ExpiryDate : null));
    }
}
