using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Catalog;
using AlaraERP.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>
/// Raises "X is low on stock: Y pieces left" (or "X is out of stock") when an active product's stock falls to its
/// alert level or below — once per drop. Restocking above the level clears it, so the next drop alerts again.
/// Products that have never had stock are skipped, so a new catalogue doesn't flood the alerts. Safe to run repeatedly.
/// </summary>
public sealed class LowStockAlertJob(IAlaraDbContext db, INotificationPublisher notifications, BusinessClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Type == ProductType.Individual && db.StockMovements.Any(m => m.ProductId == p.Id))
            .Select(p => new
            {
                p.Id, p.Name,
                InStock = db.StockBalances.Where(b => b.ProductId == p.Id).Sum(b => (int?)(b.QuantityOnHand - b.QuantityReserved)) ?? 0,
                Threshold = db.ProductInventorySettings.Where(s => s.ProductId == p.Id).Select(s => (int?)s.LowStockThreshold).FirstOrDefault()
                            ?? InventorySettingsService.DefaultThreshold,
            })
            .ToListAsync(ct);
        var alerted = await db.LowStockAlerts.ToDictionaryAsync(a => a.ProductId, ct);

        var raised = 0;
        foreach (var p in products)
        {
            var low = p.InStock <= p.Threshold;
            if (low && !alerted.ContainsKey(p.Id))
            {
                var link = $"/inventory?product={p.Id}";
                var title = p.InStock <= 0
                    ? $"{p.Name} is out of stock"
                    : $"{p.Name} is low on stock: {p.InStock} {(p.InStock == 1 ? "piece" : "pieces")} left";
                notifications.Publish(new NotificationRequest(NotificationTypes.LowStock, title,
                    (p.Threshold > 0 ? $"Alert level: {p.Threshold}. " : "") + "Receive stock to top it up.",
                    link, Permissions.Inventory.View, "Product", p.Id));
                db.LowStockAlerts.Add(new LowStockAlert(p.Id, p.InStock, p.Threshold, clock.UtcNow));
                raised++;
            }
            else if (!low && alerted.TryGetValue(p.Id, out var cleared))
            {
                db.LowStockAlerts.Remove(cleared);
            }
        }
        await db.SaveChangesAsync(ct);
        return raised;
    }
}
