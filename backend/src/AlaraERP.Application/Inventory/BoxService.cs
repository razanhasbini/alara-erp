using AlaraERP.Application.Common;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>
/// Packaging boxes: what each box is for, how many are left and when to warn. Box counts change only through
/// the actions here (never through orders), and every change is kept in the box history.
/// </summary>
public sealed class BoxService(IAlaraDbContext db, ICurrentUser currentUser, IUserDirectory users, IActivityLog activity, BusinessClock clock)
{
    public async Task<IReadOnlyList<BoxDto>> ListAsync(bool includeInactive, CancellationToken ct) =>
        await ToDtosAsync(await db.Boxes.AsNoTracking().Include(b => b.Contents)
            .Where(b => includeInactive || b.IsActive)
            .OrderByDescending(b => b.IsActive).ThenBy(b => b.Name).ToListAsync(ct), ct);

    public async Task<BoxDto> GetAsync(Guid id, CancellationToken ct)
    {
        var box = await db.Boxes.AsNoTracking().Include(b => b.Contents).FirstOrDefaultAsync(b => b.Id == id, ct)
                  ?? throw new NotFoundException("Box", id);
        return (await ToDtosAsync([box], ct))[0];
    }

    /// <summary>Active boxes made for any of these products, or for a product inside one of these bundles.</summary>
    public async Task<IReadOnlyList<BoxDto>> ForProductsAsync(IReadOnlyCollection<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var components = await db.BundleItems.AsNoTracking().Where(i => ids.Contains(i.BundleProductId))
            .Select(i => i.ComponentProductId).ToListAsync(ct);
        var all = ids.Concat(components).Distinct().ToList();
        return await ToDtosAsync(await db.Boxes.AsNoTracking().Include(b => b.Contents)
            .Where(b => b.IsActive && b.Contents.Any(c => all.Contains(c.ProductId)))
            .OrderBy(b => b.Name).ToListAsync(ct), ct);
    }

    public async Task<IReadOnlyList<BoxMovementDto>> HistoryAsync(Guid id, CancellationToken ct)
    {
        if (!await db.Boxes.AnyAsync(b => b.Id == id, ct)) throw new NotFoundException("Box", id);
        var movements = await db.BoxMovements.AsNoTracking().Where(m => m.BoxId == id)
            .OrderByDescending(m => m.CreatedAt).Take(200).ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(movements.Select(m => m.PerformedByUserId), ct);
        return movements.Select(m => new BoxMovementDto(m.Id, m.Type, m.QuantityChange, m.QuantityAfter, m.Notes,
            m.PerformedByUserId is { } u ? names.GetValueOrDefault(u) : null, m.CreatedAt)).ToList();
    }

    public async Task<BoxDto> CreateAsync(SaveBoxRequest request, CancellationToken ct)
    {
        await EnsureNameFreeAsync(request.Name, null, ct);
        await EnsureProductsExistAsync(request.Contents, ct);
        var box = new Box(request.Name, request.Notes, request.Price, request.LowStockThreshold);
        box.ReplaceContents(request.Contents.Select(c => (c.ProductId, c.Quantity)));
        db.Boxes.Add(box);
        if (request.InitialCount is { } count && count > 0)
            db.BoxMovements.Add(box.Add(count, "Starting count", currentUser.UserId, clock.UtcNow));
        activity.Record(Domain.Activity.ActivityArea.Stock,
            $"added the box {box.Name}{(box.QuantityOnHand > 0 ? $" with {box.QuantityOnHand:N0} in stock" : "")}", Link(box));
        await db.SaveChangesAsync(ct);
        return await GetAsync(box.Id, ct);
    }

    public async Task<BoxDto> UpdateAsync(Guid id, SaveBoxRequest request, CancellationToken ct)
    {
        var box = await LoadAsync(id, ct);
        await EnsureNameFreeAsync(request.Name, id, ct);
        await EnsureProductsExistAsync(request.Contents, ct);
        var threshold = box.LowStockThreshold;
        box.Update(request.Name, request.Notes, request.Price, request.LowStockThreshold);
        box.ReplaceContents(request.Contents.Select(c => (c.ProductId, c.Quantity)));
        activity.Record(Domain.Activity.ActivityArea.Stock,
            $"updated the box {box.Name}" + (threshold != box.LowStockThreshold ? $" (alert level {threshold:N0} → {box.LowStockThreshold:N0})" : ""), Link(box));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BoxDto> ChangeCountAsync(Guid id, ChangeBoxCountRequest request, CancellationToken ct)
    {
        var box = await LoadAsync(id, ct);
        if (!box.IsActive) throw new DomainException("boxes.inactive", $"{box.Name} is inactive. Activate it to change its count.");
        var before = box.QuantityOnHand;
        var movement = request.Action switch
        {
            BoxCountAction.Add => box.Add(request.Quantity, request.Notes, currentUser.UserId, clock.UtcNow),
            BoxCountAction.Remove => box.Remove(request.Quantity, request.Notes, currentUser.UserId, clock.UtcNow),
            _ => box.SetCount(request.Quantity, request.Notes, currentUser.UserId, clock.UtcNow),
        };
        db.BoxMovements.Add(movement);
        var why = string.IsNullOrWhiteSpace(request.Notes) ? "" : $" ({request.Notes.Trim()})";
        activity.Record(Domain.Activity.ActivityArea.Stock, request.Action switch
        {
            BoxCountAction.Add => $"added {request.Quantity:N0} × {box.Name}, now {box.QuantityOnHand:N0}{why}",
            BoxCountAction.Remove => $"took out {request.Quantity:N0} × {box.Name}, now {box.QuantityOnHand:N0}{why}",
            _ => $"corrected the count of {box.Name} from {before:N0} to {box.QuantityOnHand:N0}{why}",
        }, Link(box));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<BoxDto> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var box = await LoadAsync(id, ct);
        box.SetActive(isActive);
        activity.Record(Domain.Activity.ActivityArea.Stock, $"{(isActive ? "reactivated" : "deactivated")} the box {box.Name}", Link(box));
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static string Link(Box box) => $"/boxes?box={box.Id}";

    private async Task<Box> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Boxes.Include(b => b.Contents).FirstOrDefaultAsync(b => b.Id == id, ct) ?? throw new NotFoundException("Box", id);

    private async Task EnsureNameFreeAsync(string name, Guid? excludeId, CancellationToken ct)
    {
        var trimmed = name.Trim().ToLower();
        if (await db.Boxes.AnyAsync(b => b.Name.ToLower() == trimmed && b.Id != excludeId, ct))
            throw new ConflictException("boxes.duplicate_name", $"There is already a box called '{name.Trim()}'.");
    }

    private async Task EnsureProductsExistAsync(IReadOnlyList<BoxContentInput> contents, CancellationToken ct)
    {
        var ids = contents.Select(c => c.ProductId).Distinct().ToList();
        var found = await db.Products.CountAsync(p => ids.Contains(p.Id), ct);
        if (found != ids.Count) throw new DomainException("boxes.unknown_product", "One of the products in this box no longer exists.");
    }

    private async Task<List<BoxDto>> ToDtosAsync(IReadOnlyList<Box> boxes, CancellationToken ct)
    {
        var ids = boxes.SelectMany(b => b.Contents.Select(c => c.ProductId)).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Name, p.Type }).ToDictionaryAsync(p => p.Id, ct);
        return boxes.Select(b => new BoxDto(
            b.Id, b.Name, b.Notes, b.Price, b.LowStockThreshold, b.QuantityOnHand,
            StockQueryService.StatusOf(b.QuantityOnHand, b.LowStockThreshold), b.IsActive,
            b.Contents.Select(c => new BoxContentDto(c.ProductId, products[c.ProductId].Name, products[c.ProductId].Type, c.Quantity))
                .OrderBy(c => c.ProductName).ToList(),
            b.UpdatedAt)).ToList();
    }
}
