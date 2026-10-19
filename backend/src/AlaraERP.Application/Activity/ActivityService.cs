using AlaraERP.Application.Common;
using AlaraERP.Domain.Activity;
using Microsoft.EntityFrameworkCore;

namespace AlaraERP.Application.Activity;

public sealed record ActivityQuery(Guid? UserId, ActivityArea? Area, DateOnly? From, DateOnly? To, string? Search, int Page = 1, int PageSize = 50);

public sealed record ActivityEntryDto(Guid Id, DateTimeOffset At, Guid? UserId, string Who, ActivityArea Area, string Summary, string? Link);

/// <summary>Pages whose visits are worth noting ("checked the inventory"). Anything else is ignored.</summary>
public enum ViewedPage { Inventory, Analytics, Bills, Customers, StockMovements, Receipts, Orders, Dashboard }

public sealed record ViewedRequest(ViewedPage Page);

public sealed class ActivityService(IAlaraDbContext db, ICurrentUser currentUser, IActivityLog log, IUserDirectory users, BusinessClock clock)
{
    /// <summary>A visit to the same page by the same person within this window is not logged again.</summary>
    private static readonly TimeSpan ViewWindow = TimeSpan.FromMinutes(30);

    public async Task<PagedResult<ActivityEntryDto>> ListAsync(ActivityQuery query, CancellationToken ct)
    {
        var page = new PageRequest { Page = query.Page, PageSize = query.PageSize };
        var q = db.ActivityEntries.AsNoTracking();
        if (query.UserId is { } userId) q = q.Where(e => e.UserId == userId);
        if (query.Area is { } area) q = q.Where(e => e.Area == area);
        if (query.From is { } from) { var start = clock.StartOfDayUtc(from); q = q.Where(e => e.At >= start); }
        if (query.To is { } to) { var end = clock.StartOfDayUtc(to.AddDays(1)); q = q.Where(e => e.At < end); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = Text.ContainsPattern(query.Search);
            q = q.Where(e => EF.Functions.Like(e.Summary.ToLower(), term, "\\"));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(e => e.At).ThenByDescending(e => e.Id).Skip(page.Skip).Take(page.SafePageSize).ToListAsync(ct);
        var names = await users.GetDisplayNamesAsync(rows.Select(r => r.UserId), ct);
        return new PagedResult<ActivityEntryDto>(rows.Select(r => new ActivityEntryDto(r.Id, r.At, r.UserId,
                r.UserId is { } u ? names.GetValueOrDefault(u) ?? "Someone" : "The system", r.Area, r.Summary, r.Link)).ToList(),
            page.SafePage, page.SafePageSize, total);
    }

    /// <summary>Notes that the signed-in person opened a page, at most once per page per half hour.</summary>
    public async Task RecordViewAsync(ViewedRequest request, CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId) return;
        var summary = request.Page switch
        {
            ViewedPage.Inventory => "checked the inventory",
            ViewedPage.Analytics => "looked at sales & profit",
            ViewedPage.Bills => "looked at the bills",
            ViewedPage.Customers => "looked at the customers",
            ViewedPage.StockMovements => "looked at the stock movements",
            ViewedPage.Receipts => "looked at the stock receipts",
            ViewedPage.Orders => "looked at the orders",
            ViewedPage.Dashboard => "opened the dashboard",
            _ => null,
        };
        if (summary is null) return;
        var since = clock.UtcNow - ViewWindow;
        if (await db.ActivityEntries.AnyAsync(e => e.UserId == userId && e.Area == ActivityArea.Viewed && e.Summary == summary && e.At >= since, ct))
            return;
        log.Record(ActivityArea.Viewed, summary);
        await db.SaveChangesAsync(ct);
    }
}
