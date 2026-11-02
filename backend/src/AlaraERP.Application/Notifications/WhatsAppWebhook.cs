using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AlaraERP.Application.Common;
using AlaraERP.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Notifications;

/// <summary>
/// Receives Meta's delivery reports. A successful send only means Meta accepted the message; whether it reached the
/// phone arrives here later. Refuses everything until <see cref="WhatsAppOptions.AppSecret"/> and the verify token are set.
/// </summary>
public sealed class WhatsAppWebhook(IAlaraDbContext db, IOptions<WhatsAppOptions> options, ILogger<WhatsAppWebhook> logger)
{
    /// <summary>The handshake when the webhook is saved in Meta: answers only someone who knows the verify token.</summary>
    public bool AcceptsSubscription(string? mode, string? verifyToken) =>
        options.Value.DeliveryReportsReady && mode == "subscribe" && SameText(verifyToken, options.Value.WebhookVerifyToken);

    /// <summary>Checks Meta's signature ("sha256=hex"), computed over the exact bytes received with the app secret.</summary>
    public bool IsSignedByMeta(byte[] body, string? signatureHeader)
    {
        const string prefix = "sha256=";
        if (!options.Value.DeliveryReportsReady || signatureHeader is null || !signatureHeader.StartsWith(prefix, StringComparison.Ordinal)) return false;
        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(options.Value.AppSecret!), body));
        return SameText(signatureHeader[prefix.Length..].ToLowerInvariant(), expected);
    }

    /// <summary>Applies the delivery reports in a signed payload and returns how many messages it knew about.</summary>
    public async Task<int> ApplyAsync(byte[] body, CancellationToken ct)
    {
        var reports = Parse(body);
        if (reports.Count == 0) return 0;

        var ids = reports.Select(r => r.MessageId).Distinct().ToList();
        var messages = await db.WhatsAppMessages.Where(m => m.ProviderMessageId != null && ids.Contains(m.ProviderMessageId)).ToListAsync(ct);
        foreach (var report in reports)
        {
            messages.FirstOrDefault(m => m.ProviderMessageId == report.MessageId)?.ApplyDeliveryReport(report.Status, report.Error);
            if (report.Status == WhatsAppStatus.Failed)
                logger.LogWarning("WhatsApp could not deliver message {MessageId}: {Error}", report.MessageId, report.Error);
        }
        await db.SaveChangesAsync(ct);
        return messages.Count;
    }

    private List<Report> Parse(byte[] body)
    {
        var reports = new List<Report>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            foreach (var entry in Items(doc.RootElement, "entry"))
            foreach (var change in Items(entry, "changes"))
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                // Nobody reads this number's inbox in the ERP, so say so in the logs without keeping what was written.
                var inbound = Items(value, "messages").Count();
                if (inbound > 0) logger.LogInformation("WhatsApp: {Count} incoming message(s) ignored; the ERP only sends alerts.", inbound);

                foreach (var status in Items(value, "statuses"))
                {
                    var id = status.TryGetProperty("id", out var i) ? i.GetString() : null;
                    var reported = status.TryGetProperty("status", out var s) ? s.GetString() : null;
                    WhatsAppStatus? mapped = reported switch
                    {
                        "delivered" => WhatsAppStatus.Delivered,
                        "read" => WhatsAppStatus.Read,
                        "failed" => WhatsAppStatus.Failed,
                        _ => null, // "sent" adds nothing: the message is already marked sent
                    };
                    if (id is not null && mapped is { } known) reports.Add(new Report(id, known, ErrorOf(status)));
                }
            }
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "WhatsApp sent a delivery report that could not be read.");
        }
        return reports;
    }

    private static string? ErrorOf(JsonElement status)
    {
        var error = Items(status, "errors").FirstOrDefault();
        if (error.ValueKind != JsonValueKind.Object) return null;
        var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : 0;
        var title = error.TryGetProperty("title", out var t) ? t.GetString() : null;
        return WhatsAppText.Describe(code, title);
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : [];

    private static bool SameText(string? a, string? b) =>
        a is not null && b is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    private sealed record Report(string MessageId, WhatsAppStatus Status, string? Error);
}
