using System;
using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class PropertyClockTests
{
    private const string PACIFIC_ZONE = "America/Los_Angeles";

    [Test]
    public void Boundary_IsSixAmInConfiguredZone()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 7, 25, 13, 0, 0, TimeSpan.Zero));

        Assert.That(clock.GetLocalNow(PACIFIC_ZONE), Is.EqualTo(
            new DateTimeOffset(2026, 7, 25, 6, 0, 0, TimeSpan.FromHours(-7))));
    }

    [Test]
    public void Today_RemainsPropertyLocalDateWhenUtcIsFollowingDate()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 7, 26, 2, 0, 0, TimeSpan.Zero));

        Assert.That(clock.Today(PACIFIC_ZONE), Is.EqualTo(new DateOnly(2026, 7, 25)));
    }

    [Test]
    public void Today_IsStableAcrossSpringDstTransition()
    {
        var before = CreateClock(
            new DateTimeOffset(2026, 3, 8, 9, 30, 0, TimeSpan.Zero));
        var after = CreateClock(
            new DateTimeOffset(2026, 3, 8, 10, 30, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(before.Today(PACIFIC_ZONE), Is.EqualTo(new DateOnly(2026, 3, 8)));
            Assert.That(before.GetLocalNow(PACIFIC_ZONE).Offset, Is.EqualTo(TimeSpan.FromHours(-8)));
            Assert.That(after.Today(PACIFIC_ZONE), Is.EqualTo(new DateOnly(2026, 3, 8)));
            Assert.That(after.GetLocalNow(PACIFIC_ZONE).Offset, Is.EqualTo(TimeSpan.FromHours(-7)));
        });
    }

    [Test]
    public void Today_IsStableAcrossFallDstTransition()
    {
        var before = CreateClock(
            new DateTimeOffset(2026, 11, 1, 8, 30, 0, TimeSpan.Zero));
        var after = CreateClock(
            new DateTimeOffset(2026, 11, 1, 9, 30, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(before.Today(PACIFIC_ZONE), Is.EqualTo(new DateOnly(2026, 11, 1)));
            Assert.That(before.GetLocalNow(PACIFIC_ZONE).Offset, Is.EqualTo(TimeSpan.FromHours(-7)));
            Assert.That(after.Today(PACIFIC_ZONE), Is.EqualTo(new DateOnly(2026, 11, 1)));
            Assert.That(after.GetLocalNow(PACIFIC_ZONE).Offset, Is.EqualTo(TimeSpan.FromHours(-8)));
        });
    }

    [Test]
    public void AtBoundary_UsesOffsetInEffectAtTheGivenTime()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var sixAm = new TimeOnly(6, 0);

        Assert.Multiple(() =>
        {
            Assert.That(
                clock.AtBoundary(new DateOnly(2026, 3, 8), PACIFIC_ZONE, sixAm),
                Is.EqualTo(new DateTimeOffset(
                    2026, 3, 8, 6, 0, 0, TimeSpan.FromHours(-7))));
            Assert.That(
                clock.AtBoundary(new DateOnly(2026, 11, 1), PACIFIC_ZONE, sixAm),
                Is.EqualTo(new DateTimeOffset(
                    2026, 11, 1, 6, 0, 0, TimeSpan.FromHours(-8))));
        });
    }

    [Test]
    public void AtBoundary_HonoursTheRequestedTimeOfDay()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(
                clock.AtBoundary(new DateOnly(2026, 7, 31), PACIFIC_ZONE, new TimeOnly(15, 0)),
                Is.EqualTo(new DateTimeOffset(
                    2026, 7, 31, 15, 0, 0, TimeSpan.FromHours(-7))));
            Assert.That(
                clock.AtBoundary(new DateOnly(2026, 8, 2), PACIFIC_ZONE, new TimeOnly(11, 0)),
                Is.EqualTo(new DateTimeOffset(
                    2026, 8, 2, 11, 0, 0, TimeSpan.FromHours(-7))));
        });
    }

    [Test]
    public void AtBoundary_SkipsForwardThroughANonexistentLocalTime()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.That(
            clock.AtBoundary(new DateOnly(2026, 3, 8), PACIFIC_ZONE, new TimeOnly(2, 30)),
            Is.EqualTo(new DateTimeOffset(
                2026, 3, 8, 3, 0, 0, TimeSpan.FromHours(-7))));
    }

    [Test]
    public void AtBoundary_TakesTheFirstOccurrenceOfAnAmbiguousLocalTime()
    {
        var clock = CreateClock(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        Assert.That(
            clock.AtBoundary(new DateOnly(2026, 11, 1), PACIFIC_ZONE, new TimeOnly(1, 30)),
            Is.EqualTo(new DateTimeOffset(
                2026, 11, 1, 1, 30, 0, TimeSpan.FromHours(-7))));
    }

    [Test]
    public void DifferentProperties_CanHaveDifferentLocalDatesAtTheSameInstant()
    {
        var clock = CreateClock(new DateTimeOffset(2026, 7, 26, 2, 0, 0, TimeSpan.Zero));

        Assert.Multiple(() =>
        {
            Assert.That(clock.Today("America/Los_Angeles"), Is.EqualTo(new DateOnly(2026, 7, 25)));
            Assert.That(clock.Today("Europe/London"), Is.EqualTo(new DateOnly(2026, 7, 26)));
        });
    }

    private static PropertyClock CreateClock(DateTimeOffset now) =>
        new(new ManualTimeProvider(now));

    private class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.UtcNow;


        private readonly DateTimeOffset UtcNow;
    }
}
