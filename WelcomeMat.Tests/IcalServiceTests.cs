using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class IcalServiceTests
{
    private static IcalEvent MakeEvent(
        string summary = "Beach House",
        string? description = null,
        string? location = null) => new()
        {
            Uid = "reservation-11111111-1111-1111-1111-111111111111@welcomemat",
            Start = new DateTimeOffset(2026, 8, 1, 15, 0, 0, TimeSpan.FromHours(-7)),
            End = new DateTimeOffset(2026, 8, 6, 11, 0, 0, TimeSpan.FromHours(-7)),
            Summary = summary,
            Description = description,
            Location = location,
        };

    [Test]
    public void Build_EmitsUtcDateTimesNotFloatingDates()
    {
        var ical = IcalService.Build("Cal", [MakeEvent()]);

        Assert.Multiple(() =>
        {
            Assert.That(ical, Does.Contain("DTSTART:20260801T220000Z\r\n"));
            Assert.That(ical, Does.Contain("DTEND:20260806T180000Z\r\n"));
            Assert.That(ical, Does.Not.Contain("VALUE=DATE"));
        });
    }

    [Test]
    public void Build_EventEndsOnTheCheckOutDate()
    {
        var ical = IcalService.Build("Cal", [MakeEvent()]);

        Assert.That(ical, Does.Contain("DTEND:20260806"));
    }

    [Test]
    public void Build_EmitsOnlyCrlfNewlines()
    {
        var ical = IcalService.Build("Cal", [MakeEvent()]);

        for (var i = 0; i < ical.Length; i++)
        {
            if (ical[i] == '\n')
            {
                Assert.That(i > 0 && ical[i - 1] == '\r', Is.True, $"bare LF at index {i}");
            }

            if (ical[i] == '\r')
            {
                Assert.That(i + 1 < ical.Length && ical[i + 1] == '\n', Is.True, $"bare CR at index {i}");
            }
        }
    }

    [Test]
    public void Build_NormalizesLoneCrAndStripsControlCharacters()
    {
        var control = ((char)7).ToString();
        var description = "Line1\r" + control + "Line2Line3\r\nLine4";

        var ical = IcalService.Build("Cal", [MakeEvent(description: description)]);

        Assert.That(ical, Does.Contain("DESCRIPTION:Line1\\nLine2Line3\\nLine4\r\n"));
    }

    [Test]
    public void Build_EscapesBackslashSemicolonAndComma()
    {
        var ical = IcalService.Build("Cal", [MakeEvent(description: "a\\b;c,d")]);

        Assert.That(ical, Does.Contain("DESCRIPTION:a\\\\b\\;c\\,d\r\n"));
    }

    [Test]
    public void Build_OmitsEmptyDescriptionAndLocation()
    {
        var ical = IcalService.Build("Cal", [MakeEvent(description: null, location: "")]);

        Assert.Multiple(() =>
        {
            Assert.That(ical, Does.Not.Contain("DESCRIPTION:"));
            Assert.That(ical, Does.Not.Contain("LOCATION:"));
        });
    }

    [Test]
    public void Build_FoldsLongUtf8LineWithoutSplittingCodePoint()
    {
        var longName = string.Concat(Enumerable.Repeat("\U0001F389", 20));

        var ical = IcalService.Build("Cal", [MakeEvent(summary: longName)]);

        Assert.That(ical, Does.Contain("\r\n "), "expected a folded continuation line");

        var physicalLines = ical.Split("\r\n");
        foreach (var line in physicalLines)
        {
            Assert.That(Encoding.UTF8.GetByteCount(line), Is.LessThanOrEqualTo(75),
                $"physical line exceeds 75 UTF-8 octets: {line}");
        }

        // Exactly one leading space; two would change the content after unfolding.
        foreach (var line in physicalLines.Where(l => l.StartsWith(' ')))
        {
            Assert.That(line.Length == 1 || line[1] != ' ', Is.True, "double space on continuation line");
        }

        var unfolded = new List<string>();
        foreach (var line in physicalLines)
        {
            if (line.StartsWith(' ') && unfolded.Count > 0)
            {
                unfolded[^1] += line[1..];
            }
            else
            {
                unfolded.Add(line);
            }
        }
        var summaryLine = unfolded.Single(l => l.StartsWith("SUMMARY:"));
        Assert.That(summaryLine, Is.EqualTo("SUMMARY:" + longName));
    }

    [Test]
    public void Build_EscapesTrailingBackslash()
    {
        var ical = IcalService.Build("Cal", [MakeEvent(description: @"ends with\")]);

        Assert.That(ical, Does.Contain("DESCRIPTION:ends with\\\\\r\n"));
    }

    [Test]
    public void IcalNet_StillDoesNotEscapeBackslashesItself()
    {
        var calendar = new Ical.Net.Calendar { ProductId = "canary" };
        calendar.Events.Add(new Ical.Net.CalendarComponents.CalendarEvent
        {
            Uid = "canary",
            Start = new Ical.Net.DataTypes.CalDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "UTC"),
            End = new Ical.Net.DataTypes.CalDateTime(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), "UTC"),
            DtStamp = new Ical.Net.DataTypes.CalDateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "UTC"),
            Description = @"a\b",
        });

        var serialized = new Ical.Net.Serialization.CalendarSerializer().SerializeToString(calendar)!;

        Assert.That(
            serialized, Does.Contain(@"DESCRIPTION:a\b" + "\r\n"),
            "Ical.Net now escapes backslashes; remove the backslash escaping from " +
            "IcalService.SanitizeText or values will be double-escaped.");
    }

    [Test]
    public void Build_UsesTheSuppliedUid()
    {
        var ical = IcalService.Build("Cal", [MakeEvent()]);

        Assert.That(ical, Does.Contain(
            "UID:reservation-11111111-1111-1111-1111-111111111111@welcomemat\r\n"));
    }
}
