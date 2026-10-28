namespace AlaraERP.Application.Notifications;

/// <summary>WhatsApp Business (Meta Cloud API) settings, section "WhatsApp". Secrets belong in user-secrets / env vars.</summary>
public sealed class WhatsAppOptions
{
    public const string Section = "WhatsApp";

    public bool Enabled { get; set; }
    /// <summary>WhatsApp Business phone-number id from Meta (not the phone number itself).</summary>
    public string? PhoneNumberId { get; set; }
    public string? AccessToken { get; set; }
    public string ApiVersion { get; set; } = "v21.0";
    /// <summary>Who receives alerts, in international format (e.g. 96170123456).</summary>
    public List<string> Recipients { get; set; } = [];
    /// <summary>
    /// Approved message template for business-initiated messages (required by WhatsApp outside a 24-hour chat window).
    /// The template must have one body parameter, which receives the alert text. Without it, plain text is sent.
    /// </summary>
    public string? TemplateName { get; set; }
    public string TemplateLanguage { get; set; } = "en";

    public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(PhoneNumberId) && !string.IsNullOrWhiteSpace(AccessToken) && Recipients.Count > 0;
}

public sealed record NotificationRequest(
    string Type, string Title, string Body, string? Link, string? RequiredPermission,
    string? ReferenceType = null, Guid? ReferenceId = null);

/// <summary>Raises an alert: stored for the in-app Alerts list and queued for WhatsApp. Staged on the caller's unit of work.</summary>
public interface INotificationPublisher
{
    void Publish(NotificationRequest request);
}

public sealed record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error, bool Permanent);

public interface IWhatsAppSender
{
    Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct);
}

public static class NotificationTypes
{
    public const string ReservationExpired = "reservation.expired";
    public const string LowStock = "stock.low";
    public const string LowBoxes = "boxes.low";
}

/// <summary>Section "App": where the ERP is opened, used to put links in WhatsApp messages.</summary>
public sealed class AppLinks
{
    public string? FrontendUrl { get; set; }

    /// <summary>" https://…/path" when the address is configured, otherwise empty (for the end of a message).</summary>
    public string Suffix(string path) => string.IsNullOrWhiteSpace(FrontendUrl) ? "" : $" {FrontendUrl.TrimEnd('/')}{path}";
}
