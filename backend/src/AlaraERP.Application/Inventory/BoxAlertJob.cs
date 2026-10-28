using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Notifications;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Inventory;

/// <summary>
/// Raises "X is low: Y boxes left" once when an active box falls to its alert level or below; counting back above
/// the level clears it, so the next drop alerts again. Boxes never counted are skipped. Safe to run repeatedly.
/// </summary>
public sealed class BoxAlertJob(IAlaraDbContext db, INotificationPublisher notifications, BusinessClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var boxes = await db.Boxes
            .Where(b => b.IsActive && db.BoxMovements.Any(m => m.BoxId == b.Id)
                        && ((b.QuantityOnHand <= b.LowStockThreshold) != (b.LowStockAlertedAt != null)))
            .ToListAsync(ct);

        var raised = 0;
        foreach (var box in boxes)
        {
            if (box.IsLow)
            {
                var left = box.QuantityOnHand;
                notifications.Publish(new NotificationRequest(NotificationTypes.LowBoxes,
                    left <= 0 ? $"{box.Name} has run out" : $"{box.Name} is low: {left:N0} {(left == 1 ? "box" : "boxes")} left",
                    (box.LowStockThreshold > 0 ? $"Alert level: {box.LowStockThreshold:N0}. " : "") + "Order more boxes.",
                    $"/boxes?box={box.Id}", Permissions.Boxes.View, "Box", box.Id));
                box.MarkLowStockAlerted(clock.UtcNow);
                raised++;
            }
            else
            {
                box.ClearLowStockAlert();
            }
        }
        await db.SaveChangesAsync(ct);
        return raised;
    }
}
