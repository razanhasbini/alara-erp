using AlaraERP.Application.Common;
using AlaraERP.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Notifications;

public sealed record WhatsAppDeliveryDto(Guid Id, string ToPhone, WhatsAppStatus Status, int Attempts, string? LastError, DateTimeOffset? SentAt);

public sealed record NotificationDto(
    Guid Id, string Type, string Title, string Body, string? Link, DateTimeOffset CreatedAt, bool IsRead,
    IReadOnlyList<WhatsAppDeliveryDto> WhatsApp);

public sealed record NotificationQuery(bool UnreadOnly = false, int Page = 1, int PageSize = 25);

public sealed record NotificationChannelsDto(bool WhatsAppConfigured, int WhatsAppRecipients, bool UsesTemplate, int WhatsAppPending, int WhatsAppNotSent);

internal sealed class NotificationPublisher(IAlaraDbContext db, BusinessClock clock, IOptions<WhatsAppOptions> whatsApp, IOptions<AppLinks> links) : INotificationPublisher
{
    public void Publish(NotificationRequest r)
    {
        var notification = new Notification(r.Type, r.Title, r.Body, r.Link, r.RequiredPermission, r.ReferenceType, r.ReferenceId, clock.UtcNow);
        db.Notifications.Add(notification);

        var options = whatsApp.Value;
        // The app has its own "Open" button; a WhatsApp message carries the link instead.
        var text = $"{r.Title}\n{r.Body}" + (r.Link is null ? "" : links.Value.Suffix(r.Link));
        if (!options.IsConfigured)
        {
            // Kept so the Alerts page shows the message was not sent, and it can be sent once WhatsApp is connected.
            db.WhatsAppMessages.Add(new WhatsAppMessage(notification.Id, "", text, WhatsAppStatus.NotConfigured, clock.UtcNow));
            return;
        }
        foreach (var phone in options.Recipients.Select(p => new string(p.Where(char.IsDigit).ToArray())).Where(p => p.Length >= 7).Distinct())
            db.WhatsAppMessages.Add(new WhatsAppMessage(notification.Id, phone, text, WhatsAppStatus.Pending, clock.UtcNow));
    }
}

/// <summary>The in-app Alerts list. Each employee sees alerts their permissions allow and has their own read state.</summary>
public sealed class NotificationService(IAlaraDbContext db, ICurrentUser currentUser, BusinessClock clock, IOptions<WhatsAppOptions> whatsApp)
{
    public async Task<PagedResult<NotificationDto>> ListAsync(NotificationQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = Visible();
        var me = currentUser.RequiredUserId;
        if (query.UnreadOnly) q = q.Where(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me));

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id).Skip(page.Skip).Take(page.SafePageSize)
            .Select(n => new { n, Read = db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me) })
            .ToListAsync(ct);
        var ids = rows.Select(r => r.n.Id).ToList();
        var messages = await db.WhatsAppMessages.AsNoTracking().Where(m => ids.Contains(m.NotificationId)).ToListAsync(ct);

        return new PagedResult<NotificationDto>(rows.Select(r => new NotificationDto(
                r.n.Id, r.n.Type, r.n.Title, r.n.Body, r.n.Link, r.n.CreatedAt, r.Read,
                messages.Where(m => m.NotificationId == r.n.Id).OrderBy(m => m.ToPhone)
                    .Select(m => new WhatsAppDeliveryDto(m.Id, Mask(m.ToPhone), m.Status, m.Attempts, m.LastError, m.SentAt)).ToList()))
            .ToList(), page.SafePage, page.SafePageSize, total);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var me = currentUser.RequiredUserId;
        return await Visible().CountAsync(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me), ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(n => n.Id == id, ct)) throw new NotFoundException("Alert", id);
        var me = currentUser.RequiredUserId;
        if (await db.NotificationReads.AnyAsync(r => r.NotificationId == id && r.UserId == me, ct)) return;
        db.NotificationReads.Add(new NotificationRead(id, me, clock.UtcNow));
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var me = currentUser.RequiredUserId;
        var unread = await Visible().Where(n => !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.UserId == me))
            .Select(n => n.Id).ToListAsync(ct);
        foreach (var id in unread) db.NotificationReads.Add(new NotificationRead(id, me, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return unread.Count;
    }

    /// <summary>Queues every unsent message of an alert again (after WhatsApp is connected, or after a failure).</summary>
    public async Task ResendWhatsAppAsync(Guid id, CancellationToken ct)
    {
        if (!await Visible().AnyAsync(n => n.Id == id, ct)) throw new NotFoundException("Alert", id);
        var options = whatsApp.Value;
        if (!options.IsConfigured)
            throw new Domain.Common.DomainException("whatsapp.not_configured", "WhatsApp is not connected yet, so the message cannot be sent.");

        var messages = await db.WhatsAppMessages.Where(m => m.NotificationId == id).ToListAsync(ct);
        var text = messages.FirstOrDefault()?.Body ?? "";
        foreach (var m in messages.Where(m => m.Status is WhatsAppStatus.Failed))
            m.Retry();
        // An alert raised before WhatsApp was connected has a single placeholder; fan it out to today's recipients.
        foreach (var placeholder in messages.Where(m => m.Status == WhatsAppStatus.NotConfigured).ToList())
        {
            db.WhatsAppMessages.Remove(placeholder);
            foreach (var phone in options.Recipients.Select(p => new string(p.Where(char.IsDigit).ToArray())).Where(p => p.Length >= 7).Distinct())
                db.WhatsAppMessages.Add(new WhatsAppMessage(id, phone, text, WhatsAppStatus.Pending, clock.UtcNow));
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<NotificationChannelsDto> ChannelsAsync(CancellationToken ct)
    {
        var o = whatsApp.Value;
        return new NotificationChannelsDto(o.IsConfigured, o.IsConfigured ? o.Recipients.Count : 0, !string.IsNullOrWhiteSpace(o.TemplateName),
            await db.WhatsAppMessages.CountAsync(m => m.Status == WhatsAppStatus.Pending, ct),
            await db.WhatsAppMessages.CountAsync(m => m.Status == WhatsAppStatus.NotConfigured || m.Status == WhatsAppStatus.Failed, ct));
    }

    private IQueryable<Notification> Visible()
    {
        var permissions = currentUser.Permissions.ToList();
        return db.Notifications.AsNoTracking().Where(n => n.RequiredPermission == null || permissions.Contains(n.RequiredPermission));
    }

    /// <summary>Shows only the last 3 digits of a recipient's number.</summary>
    private static string Mask(string phone) => phone.Length <= 3 ? phone : new string('•', phone.Length - 3) + phone[^3..];
}

/// <summary>Sends queued WhatsApp messages. Run periodically by a background worker; safe to run repeatedly.</summary>
public sealed class WhatsAppDispatcher(IAlaraDbContext db, IWhatsAppSender sender, BusinessClock clock, ILogger<WhatsAppDispatcher> logger)
{
    public async Task<int> SendPendingAsync(int batchSize, CancellationToken ct)
    {
        var pending = await db.WhatsAppMessages.Where(m => m.Status == WhatsAppStatus.Pending)
            .OrderBy(m => m.CreatedAt).Take(batchSize).ToListAsync(ct);
        foreach (var message in pending)
        {
            var result = await sender.SendAsync(message.ToPhone, message.Body, ct);
            if (result.Success) message.MarkSent(result.ProviderMessageId, clock.UtcNow);
            else
            {
                message.MarkAttemptFailed(result.Error ?? "Unknown error", result.Permanent);
                logger.LogWarning("WhatsApp message {Id} not sent (attempt {Attempt}): {Error}", message.Id, message.Attempts, result.Error);
            }
            await db.SaveChangesAsync(ct);
        }
        return pending.Count;
    }
}
