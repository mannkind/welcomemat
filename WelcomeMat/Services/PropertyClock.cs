using System;
using System.Linq;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to read the clock in a property's own time zone.
/// </summary>
public class PropertyClock
{
    /// <summary>
    /// Initializes a new instance of the PropertyClock class.
    /// </summary>
    /// <param name="timeProvider"></param>
    public PropertyClock(TimeProvider timeProvider)
    {
        this.Clock = timeProvider;
    }

    /// <summary>
    /// The current instant, in the property's time zone.
    /// </summary>
    /// <param name="timeZoneId"></param>
    /// <returns></returns>
    public DateTimeOffset GetLocalNow(string timeZoneId) =>
        TimeZoneInfo.ConvertTime(this.Clock.GetUtcNow(), Resolve(timeZoneId));

    /// <summary>
    /// The current date, in the property's time zone.
    /// </summary>
    /// <param name="timeZoneId"></param>
    /// <returns></returns>
    public DateOnly Today(string timeZoneId) =>
        DateOnly.FromDateTime(this.GetLocalNow(timeZoneId).DateTime);

    /// <summary>
    /// The instant a check-in or check-out happens, as an offset from UTC.
    /// </summary>
    /// <param name="date"></param>
    /// <param name="timeZoneId"></param>
    /// <param name="timeOfDay"></param>
    /// <returns></returns>
    public DateTimeOffset AtBoundary(DateOnly date, string timeZoneId, TimeOnly timeOfDay)
    {
        var timeZone = Resolve(timeZoneId);
        var local = date.ToDateTime(timeOfDay, DateTimeKind.Unspecified);

        // Spring-forward skips this wall clock time entirely; walk to the next one that exists.
        while (timeZone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        // Fall-back makes it happen twice; take the later offset so the stay isn't cut short.
        var offset = timeZone.IsAmbiguousTime(local)
            ? timeZone.GetAmbiguousTimeOffsets(local).Max()
            : timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    /// <summary>
    /// The clock used internally.
    /// </summary>
    private readonly TimeProvider Clock;

    /// <summary>
    /// Look up a time zone by its IANA id.
    /// </summary>
    /// <param name="timeZoneId"></param>
    /// <returns></returns>
    private static TimeZoneInfo Resolve(string timeZoneId) =>
        TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
}
