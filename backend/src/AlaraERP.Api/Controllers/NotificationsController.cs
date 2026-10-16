using AlaraERP.Application.Common;
using AlaraERP.Application.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace AlaraERP.Api.Controllers;

/// <summary>The in-app Alerts list: everything also sent (or meant to be sent) on WhatsApp. Visibility follows permissions.</summary>
[Route("api/notifications")]
public sealed class NotificationsController(NotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public Task<PagedResult<NotificationDto>> List([FromQuery] NotificationQuery query, CancellationToken ct) => notifications.ListAsync(query, ct);

    public sealed record UnreadCountDto(int Count);

    [HttpGet("unread-count")]
    public async Task<UnreadCountDto> UnreadCount(CancellationToken ct) => new(await notifications.UnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<UnreadCountDto> MarkAllRead(CancellationToken ct) => new(await notifications.MarkAllReadAsync(ct));

    /// <summary>Send again the WhatsApp messages of an alert that were not delivered.</summary>
    [HttpPost("{id:guid}/resend-whatsapp")]
    [ProducesResponseType(204)]
    public async Task<IActionResult> ResendWhatsApp(Guid id, CancellationToken ct)
    {
        await notifications.ResendWhatsAppAsync(id, ct);
        return NoContent();
    }

    [HttpGet("channels")]
    public Task<NotificationChannelsDto> Channels(CancellationToken ct) => notifications.ChannelsAsync(ct);
}
