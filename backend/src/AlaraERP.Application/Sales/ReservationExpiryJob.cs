using AlaraERP.Application.Common;
using AlaraERP.Application.Common.Security;
using AlaraERP.Application.Notifications;
using AlaraERP.Domain.Sales;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Sales;

/// <summary>
/// Raises one "reservation time is up" alert per reserved order whose deadline passed. Nothing is released
/// automatically: someone decides whether to sell, extend or cancel. Safe to run repeatedly.
/// </summary>
public sealed class ReservationExpiryJob(IAlaraDbContext db, INotificationPublisher notifications, BusinessClock clock)
{
    public async Task<int> RunAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var due = await db.Orders.Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Reserved && o.ReservedUntil <= now && o.ReservationExpiryNotifiedAt == null)
            .OrderBy(o => o.ReservedUntil).Take(100)
            .ToListAsync(ct);

        foreach (var order in due)
        {
            var items = string.Join(", ", order.Items.OrderBy(i => i.ProductName).Select(i => $"{i.Quantity} × {i.ProductName}{(i.IsGift ? " (gift)" : "")}"));
            var deadline = clock.ToBusinessDateTime(order.ReservedUntil!.Value);
            var link = $"/orders/{order.Id}";
            notifications.Publish(new NotificationRequest(
                NotificationTypes.ReservationExpired,
                $"Reservation time is up: {order.OrderNumber}",
                $"{order.RecipientName} ({order.RecipientPhone}) — reserved until {deadline:dd MMM HH:mm}. Items: {items}. " +
                $"Sell it, give more time, or cancel to free the stock.",
                link, Permissions.Orders.View, "Order", order.Id));
            order.MarkReservationExpiryNotified(now);
        }
        if (due.Count > 0) await db.SaveChangesAsync(ct);
        return due.Count;
    }
}
