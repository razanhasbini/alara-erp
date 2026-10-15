using AlaraERP.Application.Common;
using AlaraERP.Application.Inventory.Contracts;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

public sealed class WarehouseService(IAlaraDbContext db) : IWarehouseDirectory
{
    public async Task<IReadOnlyList<WarehouseDto>> ListAsync(bool includeInactive, CancellationToken ct) =>
        await Project(db.Warehouses.AsNoTracking().Where(w => includeInactive || w.IsActive))
            .OrderByDescending(w => w.IsActive).ThenBy(w => w.Code)
            .ToListAsync(ct);

    public async Task<WarehouseDto> GetAsync(Guid id, CancellationToken ct) =>
        await Project(db.Warehouses.AsNoTracking().Where(w => w.Id == id)).FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Warehouse", id);

    public async Task<WarehouseDto> CreateAsync(WarehouseRequest request, CancellationToken ct)
    {
        var warehouse = new Warehouse(request.Name, request.Code, request.Address);
        await EnsureCodeFreeAsync(warehouse.Code, null, ct);
        if (!request.IsActive) warehouse.SetActive(false);
        db.Warehouses.Add(warehouse);
        await db.SaveChangesAsync(ct);
        return await GetAsync(warehouse.Id, ct);
    }

    public async Task<WarehouseDto> UpdateAsync(Guid id, WarehouseRequest request, CancellationToken ct)
    {
        var warehouse = await db.Warehouses.FirstOrDefaultAsync(w => w.Id == id, ct) ?? throw new NotFoundException("Warehouse", id);
        warehouse.Update(request.Name, request.Code, request.Address);
        await EnsureCodeFreeAsync(warehouse.Code, id, ct);

        if (warehouse.IsActive && !request.IsActive)
        {
            var hasStock = await db.StockBalances.AnyAsync(b => b.WarehouseId == id && (b.QuantityOnHand > 0 || b.QuantityReserved > 0), ct);
            if (hasStock)
                throw new ConflictException("warehouses.has_stock",
                    "This warehouse still holds or reserves stock. Transfer or adjust it to zero before deactivating.");
        }
        warehouse.SetActive(request.IsActive);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<WarehouseInfo?> FindAsync(Guid id, CancellationToken ct) =>
        await db.Warehouses.AsNoTracking().Where(w => w.Id == id)
            .Select(w => new WarehouseInfo(w.Id, w.Name, w.Code, w.IsActive)).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, WarehouseInfo>> GetAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Warehouses.AsNoTracking().Where(w => list.Contains(w.Id))
            .Select(w => new WarehouseInfo(w.Id, w.Name, w.Code, w.IsActive))
            .ToDictionaryAsync(w => w.Id, ct);
    }

    private async Task EnsureCodeFreeAsync(string code, Guid? excludeId, CancellationToken ct)
    {
        if (await db.Warehouses.AnyAsync(w => w.Code == code && w.Id != excludeId, ct))
            throw new ConflictException("warehouses.duplicate_code", $"Another warehouse already uses code '{code}'.");
    }

    private IQueryable<WarehouseDto> Project(IQueryable<Warehouse> q) => q.Select(w => new WarehouseDto(
        w.Id, w.Name, w.Code, w.Address, w.IsActive,
        db.StockBalances.Where(b => b.WarehouseId == w.Id).Sum(b => (int?)b.QuantityOnHand) ?? 0,
        db.StockBalances.Where(b => b.WarehouseId == w.Id).Sum(b => (int?)b.QuantityReserved) ?? 0,
        db.StockBalances.Count(b => b.WarehouseId == w.Id && b.QuantityOnHand > 0),
        w.UpdatedAt));
}
