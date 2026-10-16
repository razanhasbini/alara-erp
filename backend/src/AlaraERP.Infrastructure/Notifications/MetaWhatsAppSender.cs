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
                    components = new[] { new { type = "body", parameters = new[] { new { type = "text", text = body.Replace('\n', ' ') } } } },
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
                using var doc = JsonDocument.Parse(text);
                var id = doc.RootElement.TryGetProperty("messages", out var m) && m.GetArrayLength() > 0 ? m[0].GetProperty("id").GetString() : null;
                return new WhatsAppSendResult(true, id, null, false);
            }
            // 4xx (bad number, expired token, template not approved) will not fix itself by retrying; 429/5xx might.
            var permanent = (int)response.StatusCode is >= 400 and < 500 && response.StatusCode != HttpStatusCode.TooManyRequests;
            return new WhatsAppSendResult(false, null, $"{(int)response.StatusCode}: {Trim(text)}", permanent);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new WhatsAppSendResult(false, null, ex.Message, Permanent: false);
        }
    }

    private static string Trim(string s) => s.Length > 500 ? s[..500] : s;
}
