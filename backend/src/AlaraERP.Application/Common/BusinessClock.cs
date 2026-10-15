using Microsoft.Extensions.Options;

namespace AlaraERP.Application.Common;

/// <summary>"Now" in UTC for timestamps, and "today" in the business time zone for dates such as expiry.</summary>
public sealed class BusinessClock(TimeProvider time, IOptions<ErpOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.TimeZone);

    public DateTimeOffset UtcNow => time.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(UtcNow, _zone).DateTime);

    /// <summary>The UTC instant at which the given local business date starts.</summary>
    public DateTimeOffset StartOfDayUtc(DateOnly date)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local)).ToUniversalTime();
    }

    public DateOnly ToBusinessDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, _zone).DateTime);
}
