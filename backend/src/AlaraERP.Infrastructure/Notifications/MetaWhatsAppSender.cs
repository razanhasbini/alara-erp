using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AlaraERP.Application.Notifications;
using Microsoft.Extensions.Options;

namespace AlaraERP.Infrastructure.Notifications;

/// <summary>
/// Sends WhatsApp messages through Meta's WhatsApp Business Cloud API.
/// With <c>WhatsApp:TemplateName</c> set, an approved template is used (required for messages the business starts);
/// otherwise plain text, which WhatsApp only delivers within 24 hours of the recipient's last message.
/// </summary>
internal sealed class MetaWhatsAppSender(HttpClient http, IOptions<WhatsAppOptions> options) : IWhatsAppSender
{
    public async Task<WhatsAppSendResult> SendAsync(string toPhone, string body, CancellationToken ct)
    {
        var o = options.Value;
        if (!o.IsConfigured)
            return new WhatsAppSendResult(false, null, "WhatsApp is not connected.", Permanent: false);

        object payload = string.IsNullOrWhiteSpace(o.TemplateName)
            ? new { messaging_product = "whatsapp", to = toPhone, type = "text", text = new { preview_url = false, body } }
            : new
            {
                messaging_product = "whatsapp", to = toPhone, type = "template",
                template = new
                {
                    name = o.TemplateName, language = new { code = o.TemplateLanguage },
                    components = new[] { new { type = "body", parameters = new[] { new { type = "text", text = WhatsAppText.ForTemplate(body) } } } },
                },
            };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://graph.facebook.com/{o.ApiVersion}/{o.PhoneNumberId}/messages")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);

        try
        {
            using var response = await http.SendAsync(request, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                return new WhatsAppSendResult(true, MessageId(text), null, false);
            }
            var (code, message) = ErrorOf(text);
            // Bad number, expired token, template not approved: retrying won't fix these. Rate limits and outages might pass.
            var temporary = response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500 || (code is { } c && WhatsAppText.IsTemporary(c));
            var error = code is { } known ? WhatsAppText.Describe(known, message) : $"{(int)response.StatusCode}: {Trim(text)}";
            return new WhatsAppSendResult(false, null, error, !temporary);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new WhatsAppSendResult(false, null, ex.Message, Permanent: false);
        }
    }

    /// <summary>The send has already succeeded here, so an unreadable reply must not make the message go out again.</summary>
    private static string? MessageId(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            return doc.RootElement.TryGetProperty("messages", out var m) && m.GetArrayLength() > 0 ? m[0].GetProperty("id").GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static (int? Code, string? Message) ErrorOf(string reply)
    {
        try
        {
            using var doc = JsonDocument.Parse(reply);
            if (!doc.RootElement.TryGetProperty("error", out var error)) return (null, null);
            var code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : (int?)null;
            var detail = error.TryGetProperty("error_data", out var data) && data.TryGetProperty("details", out var d) ? d.GetString() : null;
            return (code, detail ?? (error.TryGetProperty("message", out var m) ? m.GetString() : null));
        }
        catch (JsonException)
        {
            return (null, null);
        }
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s;
}
