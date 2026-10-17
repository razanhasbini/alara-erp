namespace AlaraERP.Application.Common;

/// <summary>Business configuration (section "Erp").</summary>
public sealed class ErpOptions
{
    public const string Section = "Erp";

    /// <summary>All prices and order totals are in this currency.</summary>
    public string BaseCurrency { get; set; } = "USD";

    /// <summary>Used to decide what "today" is for expiry dates and daily reports.</summary>
    public string TimeZone { get; set; } = "Asia/Beirut";

    /// <summary>Batches expiring within this many days are flagged.</summary>
    public int ExpiryWarningDays { get; set; } = 60;
}
