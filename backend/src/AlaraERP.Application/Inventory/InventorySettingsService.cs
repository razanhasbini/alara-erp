using AlaraERP.Application.Common;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Common;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>Per-product inventory policy (low-stock threshold, expiry tracking), set by managers.</summary>
public sealed class InventorySettingsService(IAlaraDbContext db, ICurrentUser currentUser, IActivityLog activity)
{
    /// <summary>Defaults for products nobody has configured yet.</summary>
    public const int DefaultThreshold = 0;
    public const bool DefaultTracksExpiry = true;

    public async Task<InventorySettingsDto> GetAsync(Guid productId, CancellationToken ct)
    {
        await EnsureStockableAsync(productId, ct);
        var settings = await db.ProductInventorySettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProductId == productId, ct);
        return ToDto(productId, settings);
    }

    public async Task<InventorySettingsDto> UpdateAsync(Guid productId, UpdateInventorySettingsRequest request, CancellationToken ct)
    {
        await EnsureStockableAsync(productId, ct);
        var settings = await db.ProductInventorySettings.FirstOrDefaultAsync(s => s.ProductId == productId, ct);
        if (settings is null)
        {
            settings = new ProductInventorySettings(productId, request.LowStockThreshold, request.TracksExpiry);
            db.ProductInventorySettings.Add(settings);
        }
        settings.Update(request.LowStockThreshold, request.TracksExpiry, currentUser.UserId);
        var productName = await db.Products.Where(p => p.Id == productId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "a product";
        activity.Record(Domain.Activity.ActivityArea.Stock, request.LowStockThreshold > 0
            ? $"set the stock alert for {productName} to {request.LowStockThreshold}"
            : $"set {productName} to alert only when it runs out", $"/inventory?product={productId}");
        await db.SaveChangesAsync(ct);
        return ToDto(productId, settings);
    }

    internal static InventorySettingsDto ToDto(Guid productId, ProductInventorySettings? s) => s is null
        ? new InventorySettingsDto(productId, DefaultThreshold, DefaultTracksExpiry, IsDefault: true, null)
        : new InventorySettingsDto(productId, s.LowStockThreshold, s.TracksExpiry, IsDefault: false, s.UpdatedAt);

    private async Task EnsureStockableAsync(Guid productId, CancellationToken ct)
    {
        var type = await db.Products.Where(p => p.Id == productId).Select(p => (ProductType?)p.Type).FirstOrDefaultAsync(ct)
                   ?? throw new NotFoundException("Product", productId);
        if (type == ProductType.Bundle)
            throw new DomainException("inventory.bundle_not_stocked",
                "Bundles have no stock of their own; their availability comes from their components.");
    }
}
