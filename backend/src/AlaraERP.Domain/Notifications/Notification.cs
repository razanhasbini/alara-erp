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

/// <summary>Sent means Meta accepted the message; Delivered, Read and a late Failed come from Meta's delivery reports.</summary>
public enum WhatsAppStatus { Pending, Sent, Failed, NotConfigured, Delivered, Read }

/// <summary>One WhatsApp message to one recipient for an alert (an outbox: sent by a background worker, retried on failure).</summary>
public sealed class WhatsAppMessage
{
    public const int MaxAttempts = 5;

    /// <summary>How long to wait after each failed attempt. An alert is raised only once, so a Meta outage must not lose it.</summary>
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromHours(1)];

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
    public DateTimeOffset? NextAttemptAt { get; private set; }

    public void MarkSent(string? providerMessageId, DateTimeOffset at)
    {
        Attempts++;
        Status = WhatsAppStatus.Sent;
        ProviderMessageId = providerMessageId;
        SentAt = at;
        LastError = null;
        NextAttemptAt = null;
    }

    /// <summary>Records a failed attempt; gives up (Failed) after <see cref="MaxAttempts"/> or on a permanent error.</summary>
    public void MarkAttemptFailed(string error, bool permanent, DateTimeOffset at)
    {
        Attempts++;
        LastError = Fit(error);
        Status = permanent || Attempts >= MaxAttempts ? WhatsAppStatus.Failed : WhatsAppStatus.Pending;
        NextAttemptAt = Status == WhatsAppStatus.Pending ? at + RetryDelays[Attempts - 1] : null;
    }

    /// <summary>Applies a delivery report from Meta. Reports can arrive out of order, so a message only ever moves forward.</summary>
    public void ApplyDeliveryReport(WhatsAppStatus reported, string? error)
    {
        switch (reported)
        {
            case WhatsAppStatus.Delivered when Status == WhatsAppStatus.Sent:
                Status = WhatsAppStatus.Delivered;
                break;
            case WhatsAppStatus.Read when Status is WhatsAppStatus.Sent or WhatsAppStatus.Delivered:
                Status = WhatsAppStatus.Read;
                break;
            case WhatsAppStatus.Failed when Status == WhatsAppStatus.Sent:
                Status = WhatsAppStatus.Failed;
                LastError = Fit(error ?? "WhatsApp could not deliver this message.");
                break;
        }
    }

    /// <summary>Queues a message that could not be sent (not connected / failed) to be sent again.</summary>
    public void Retry()
    {
        Status = WhatsAppStatus.Pending;
        Attempts = 0;
        LastError = null;
        ProviderMessageId = null; // a late report about the old attempt must not change the new one
        SentAt = null;
        NextAttemptAt = null;
    }

    private static string Fit(string error) => error.Length > 1000 ? error[..1000] : error;
}
