namespace AlaraERP.Domain.Activity;

/// <summary>Which part of the business an activity entry is about (for filtering the log).</summary>
public enum ActivityArea { Orders, Customers, Stock, Products, Bills, Employees, SignIn, Settings, Viewed }

/// <summary>
/// One line in the administrator's activity log, written in plain words, e.g. "entered order AL-000058 for Rima Saad:
/// 2 × Miracle Mask". The person is stored separately and shown in front of it. Append-only: entries are never edited.
/// </summary>
public sealed class ActivityEntry
{
    public const int SummaryMaxLength = 2000;

    private ActivityEntry() { }

    public ActivityEntry(Guid? userId, ActivityArea area, string summary, string? link, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        UserId = userId;
        Area = area;
        Summary = summary.Length > SummaryMaxLength ? summary[..(SummaryMaxLength - 1)] + "…" : summary;
        Link = link;
        At = at;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset At { get; private set; }
    public Guid? UserId { get; private set; }
    public ActivityArea Area { get; private set; }
    public string Summary { get; private set; } = "";
    /// <summary>Where in the app the thing can be opened, e.g. "/orders/{id}".</summary>
    public string? Link { get; private set; }
}
