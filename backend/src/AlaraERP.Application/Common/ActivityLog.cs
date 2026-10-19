using AlaraERP.Domain.Activity;

namespace AlaraERP.Application.Common;

/// <summary>
/// Writes a plain-language line to the administrator's activity log. The line is staged on the current unit of work,
/// so it is saved together with the change it describes (or not at all if that change fails).
/// </summary>
public interface IActivityLog
{
    /// <param name="summary">What happened, starting with a verb and without the person: "added customer Nour Haddad".</param>
    /// <param name="userId">Who did it; defaults to the signed-in user (pass it when nobody is signed in yet, e.g. at login).</param>
    void Record(ActivityArea area, string summary, string? link = null, Guid? userId = null);
}

internal sealed class ActivityLog(IAlaraDbContext db, ICurrentUser currentUser, BusinessClock clock) : IActivityLog
{
    public void Record(ActivityArea area, string summary, string? link = null, Guid? userId = null) =>
        db.ActivityEntries.Add(new ActivityEntry(userId ?? currentUser.UserId, area, summary, link, clock.UtcNow));
}

/// <summary>Formatting shared by the activity lines.</summary>
public static class ActivityText
{
    public static string Money(decimal amount) => $"${amount:0.00}";
    public static string Units(int n) => n == 1 ? "1 unit" : $"{n} units";
    public static string Items(IEnumerable<(int Quantity, string Name)> items) =>
        string.Join(", ", items.Select(i => $"{i.Quantity} × {i.Name}"));
}
