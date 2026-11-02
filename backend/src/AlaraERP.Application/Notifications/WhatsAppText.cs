using System.Text.RegularExpressions;

namespace AlaraERP.Application.Notifications;

/// <summary>Meta's rules for the text that fills a template's {{1}}, and what its error codes mean in plain words.</summary>
public static partial class WhatsAppText
{
    /// <summary>Meta allows 1024 characters for the whole template body, including its fixed words.</summary>
    public const int MaxTemplateText = 900;

    /// <summary>Template text may not contain line breaks, tabs or long runs of spaces. A long alert keeps its link.</summary>
    public static string ForTemplate(string text)
    {
        var flat = Spaces().Replace(LineBreaks().Replace(text.Trim(), " - "), " ");
        if (flat.Length <= MaxTemplateText) return flat;

        var lastSpace = flat.LastIndexOf(' ');
        var link = lastSpace > 0 && flat[(lastSpace + 1)..].StartsWith("http", StringComparison.Ordinal) ? flat[lastSpace..] : "";
        return flat[..(MaxTemplateText - link.Length - 1)].TrimEnd() + "…" + link;
    }

    /// <summary>Rate limits and outages on Meta's side: worth trying again later, even when sent back as a 400.</summary>
    public static bool IsTemporary(int code) => code is 1 or 2 or 4 or 80007 or 130429 or 131000 or 131016 or 131056 or 133004;

    public static string Describe(int code, string? fallback) => code switch
    {
        190 => "The WhatsApp access token has expired or is wrong. Create a new permanent token in Meta and update WhatsApp__AccessToken.",
        10 or 200 => "The WhatsApp access token doesn't have permission to send. Give it whatsapp_business_messaging in Meta.",
        100 => "Meta rejected the request. Check WhatsApp__PhoneNumberId.",
        130472 or 131049 => "Meta held this message back to protect the recipient from too many business messages.",
        131021 => "This number is the business number itself. Alerts must go to a different number.",
        131026 => "WhatsApp couldn't deliver this. The number may not be on WhatsApp, or its app may need updating.",
        131030 => "This number isn't on the allowed list of Meta's test number. Add it in Meta's API setup page.",
        131031 => "The WhatsApp Business account is locked. Check it in Meta Business settings.",
        131042 => "Meta has a billing problem on this account. Check the payment method in WhatsApp Manager.",
        131047 => "No message template is set, and this number hasn't messaged the business in the last 24 hours. Set WhatsApp__TemplateName.",
        132000 => "The message template should have exactly one {{1}}. Check it in WhatsApp Manager.",
        132001 => "The message template wasn't found in this language. Check WhatsApp__TemplateName, WhatsApp__TemplateLanguage and that Meta approved it.",
        132015 or 132016 => "Meta paused or disabled the message template. Check it in WhatsApp Manager.",
        133010 => "The business number isn't registered with the WhatsApp API yet. Finish its setup in Meta.",
        _ when IsTemporary(code) => "WhatsApp is busy or temporarily unavailable.",
        _ => string.IsNullOrWhiteSpace(fallback) ? $"WhatsApp error {code}." : $"WhatsApp error {code}: {fallback}",
    };

    [GeneratedRegex(@"\s*[\r\n]+\s*")]
    private static partial Regex LineBreaks();

    [GeneratedRegex(@"[ \t]+")]
    private static partial Regex Spaces();
}
