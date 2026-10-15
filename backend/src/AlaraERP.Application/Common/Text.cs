using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AlaraERP.Application.Common;

public static partial class Text
{
    /// <summary>"ÀLara's Pouch" → "alaras-pouch".</summary>
    public static string Slugify(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is '\'' or '’') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        var slug = NonAlphanumeric().Replace(sb.ToString(), "-").Trim('-');
        return slug.Length > 0 ? slug[..Math.Min(slug.Length, 180)] : "item";
    }

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    /// <summary>
    /// Escapes LIKE wildcards in user input and wraps it for a case-insensitive "contains" search
    /// (compare against the lower-cased column with escape character '\').
    /// </summary>
    public static string ContainsPattern(string term) =>
        "%" + term.Trim().ToLowerInvariant().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();
}
