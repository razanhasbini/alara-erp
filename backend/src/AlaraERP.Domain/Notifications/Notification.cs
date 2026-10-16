namespace AlaraERP.Domain.Notifications;

/// <summary>
/// An alert raised by the system (e.g. "reservation time is up"). Every alert is kept here so employees can review
/// it in the app even if they missed the WhatsApp message. Visible to employees holding <see cref="RequiredPermission"/>.
/// </summary>
public sealed class Notification
{
    private Notification() { }

    public Notification(string type, string title, string body, string? link, string? requiredPermission,
        string? referenceType, Guid? referenceId, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        Type = type;
        Title = title.Length > 200 ? title[..200] : title;
        Body = body.Length > 2000 ? body[..2000] : body;
        Link = link;
        RequiredPermission = requiredPermission;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Type { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Body { get; private set; } = "";
    /// <summary>Path inside the ERP, e.g. /orders/{id}.</summary>
    public string? Link { get; private set; }
    public string? RequiredPermission { get; private set; }
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
}

/// <summary>Per-employee "seen" marker.</summary>
public sealed class NotificationRead
{
    private NotificationRead() { }

    public NotificationRead(Guid notificationId, Guid userId, DateTimeOffset readAt)
    {
        NotificationId = notificationId;
        UserId = userId;
        ReadAt = readAt;
    }

    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset ReadAt { get; private set; }
}

public enum WhatsAppStatus { Pending, Sent, Failed, NotConfigured }

/// <summary>One WhatsApp message to one recipient for an alert (an outbox: sent by a background worker, retried on failure).</summary>
public sealed class WhatsAppMessage
{
    public const int MaxAttempts = 5;

    private WhatsAppMessage() { }

    public WhatsAppMessage(Guid notificationId, string toPhone, string body, WhatsAppStatus status, DateTimeOffset createdAt)
    {
        Id = Guid.CreateVersion7();
        NotificationId = notificationId;
        ToPhone = toPhone;
        Body = body.Length > 4000 ? body[..4000] : body;
        Status = status;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public Guid NotificationId { get; private set; }
    public string ToPhone { get; private set; } = "";
    public string Body { get; private set; } = "";
    public WhatsAppStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }
    public string? ProviderMessageId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }

    public void MarkSent(string? providerMessageId, DateTimeOffset at)
    {
        Attempts++;
        Status = WhatsAppStatus.Sent;
        ProviderMessageId = providerMessageId;
        SentAt = at;
        LastError = null;
    }

    /// <summary>Records a failed attempt; gives up (Failed) after <see cref="MaxAttempts"/> or on a permanent error.</summary>
    public void MarkAttemptFailed(string error, bool permanent)
    {
        Attempts++;
        LastError = error.Length > 1000 ? error[..1000] : error;
        Status = permanent || Attempts >= MaxAttempts ? WhatsAppStatus.Failed : WhatsAppStatus.Pending;
    }

    /// <summary>Queues a message that could not be sent (not connected / failed) to be sent again.</summary>
    public void Retry()
    {
        Status = WhatsAppStatus.Pending;
        Attempts = 0;
        LastError = null;
    }
}
