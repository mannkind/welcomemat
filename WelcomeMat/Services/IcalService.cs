using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;

namespace WelcomeMat.Services;

/// <summary>
/// One VEVENT, before it is serialized.
/// </summary>
public record IcalEvent
{
    public string Uid { get; init; } = string.Empty;

    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public string Summary { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string? Location { get; init; }
}

/// <summary>
/// An class representing a way to serialize reservations as an ical feed.
/// </summary>
public static class IcalService
{
    /// <summary>
    /// Serialize the events as a published calendar.
    /// </summary>
    /// <param name="calendarName"></param>
    /// <param name="events"></param>
    /// <returns></returns>
    public static string Build(string calendarName, IReadOnlyList<IcalEvent> events)
    {
        var calendar = new Calendar
        {
            ProductId = "-//WelcomeMat//WelcomeMat//EN",
            Scale = "GREGORIAN",
            Method = "PUBLISH",
        };

        calendar.AddProperty("X-WR-CALNAME", SanitizeText(calendarName));
        calendar.AddProperty("X-PUBLISHED-TTL", "PT1H");

        var refreshInterval = new CalendarProperty("REFRESH-INTERVAL", "PT1H");
        refreshInterval.AddParameter("VALUE", "DURATION");
        calendar.Properties.Add(refreshInterval);

        var stamp = new CalDateTime(DateTime.UtcNow, "UTC");

        foreach (var e in events)
        {
            calendar.Events.Add(new CalendarEvent
            {
                Uid = e.Uid,
                Start = ToUtc(e.Start),
                End = ToUtc(e.End),
                Summary = SanitizeText(e.Summary),
                Description = NullIfEmpty(e.Description),
                Location = NullIfEmpty(e.Location),
                DtStamp = stamp,
            });
        }

        return new CalendarSerializer().SerializeToString(calendar) ?? "";
    }

    /// <summary>
    /// Mint a feed token; the URL is the only thing guarding the feed.
    /// </summary>
    /// <returns></returns>
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// Convert an instant to a UTC calendar date-time.
    /// </summary>
    /// <param name="instant"></param>
    /// <returns></returns>
    private static CalDateTime ToUtc(DateTimeOffset instant) =>
        new(instant.UtcDateTime, "UTC");

    /// <summary>
    /// Drop empty values so the property is omitted rather than written blank.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static string? NullIfEmpty(string? value)
    {
        var sanitized = SanitizeText(value ?? "");
        return string.IsNullOrEmpty(sanitized) ? null : sanitized;
    }

    /// <summary>
    /// Strip the control characters that would break the serialized feed.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static string SanitizeText(string value)
    {
        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');

        var filtered = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            if (ch == '\\')
            {
                filtered.Append("\\\\");
            }
            else if (ch == '\n' || ch >= ' ')
            {
                filtered.Append(ch);
            }
        }

        return filtered.ToString();
    }
}
