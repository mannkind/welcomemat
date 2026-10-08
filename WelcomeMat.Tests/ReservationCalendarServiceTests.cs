using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class ReservationCalendarServiceTests
{
    private const string PACIFIC_ZONE = "America/Los_Angeles";
    private const string EASTERN_ZONE = "America/New_York";

    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;
    private ReservationCalendarService Service = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .Options);
        this.Service = new ReservationCalendarService(
            new StayTimesService(this.Db),
            new PropertyClock(TimeProvider.System));
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    [Test]
    public async Task Events_SpanCheckInTimeThroughCheckOutTimeInThePropertyZone()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        var reservation = MakeReservation(property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));

        var events = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single().Start, Is.EqualTo(
                new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero)));
            Assert.That(events.Single().End, Is.EqualTo(
                new DateTimeOffset(2026, 8, 2, 18, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Events_UsePerPropertyTimeOverrides()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        property.CheckInTime = new TimeOnly(17, 0);
        property.CheckOutTime = new TimeOnly(9, 0);
        var reservation = MakeReservation(property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));

        var events = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single().Start, Is.EqualTo(
                new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
            Assert.That(events.Single().End, Is.EqualTo(
                new DateTimeOffset(2026, 8, 2, 16, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Events_ResolveEachReservationAgainstItsOwnPropertyZone()
    {
        var pacific = MakeProperty("West", PACIFIC_ZONE);
        var eastern = MakeProperty("East", EASTERN_ZONE);
        var westStay = MakeReservation(pacific, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));
        var eastStay = MakeReservation(eastern, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));

        var events = await this.Service.BuildEventsAsync([westStay, eastStay], includeGuest: false);

        Assert.Multiple(() =>
        {
            Assert.That(events[0].Start, Is.EqualTo(new DateTimeOffset(2026, 7, 31, 22, 0, 0, TimeSpan.Zero)));
            Assert.That(events[1].Start, Is.EqualTo(new DateTimeOffset(2026, 7, 31, 19, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Events_ResolveAcrossADstTransition()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        var reservation = MakeReservation(property, new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 8));

        var events = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.Multiple(() =>
        {
            // Before the jump: still -08:00.
            Assert.That(events.Single().Start, Is.EqualTo(
                new DateTimeOffset(2026, 3, 6, 23, 0, 0, TimeSpan.Zero)));
            // After the jump: -07:00.
            Assert.That(events.Single().End, Is.EqualTo(
                new DateTimeOffset(2026, 3, 8, 18, 0, 0, TimeSpan.Zero)));
        });
    }

    [Test]
    public async Task Events_DoNotOverlapOnASameDayTurnover()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        var departing = MakeReservation(property, new DateOnly(2026, 7, 28), new DateOnly(2026, 7, 31));
        var arriving = MakeReservation(property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));

        var events = await this.Service.BuildEventsAsync([departing, arriving], includeGuest: false);

        Assert.That(events[0].End, Is.LessThan(events[1].Start));
    }

    [Test]
    public async Task Events_CarryNotesAsDescriptionAndAddressAsLocation()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        property.Address = "123 Pine Rd";
        var reservation = MakeReservation(
            property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2), notes: "Bring firewood");

        var events = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single().Description, Is.EqualTo("Bring firewood"));
            Assert.That(events.Single().Location, Is.EqualTo("123 Pine Rd"));
        });
    }

    [Test]
    public async Task Events_IncludeGuestNameOnlyWhenRequested()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        var user = new User { Id = Guid.NewGuid(), Email = "g@example.com", Name = "Guest Tester" };
        var reservation = MakeReservation(property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));
        reservation.User = user;

        var withGuest = await this.Service.BuildEventsAsync([reservation], includeGuest: true);
        var withoutGuest = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.Multiple(() =>
        {
            Assert.That(withGuest.Single().Summary, Does.Contain("Guest Tester"));
            Assert.That(withoutGuest.Single().Summary, Is.EqualTo("Cabin"));
        });
    }

    [Test]
    public async Task Events_UseAStableUidPerReservation()
    {
        var property = MakeProperty("Cabin", PACIFIC_ZONE);
        var reservation = MakeReservation(property, new DateOnly(2026, 7, 31), new DateOnly(2026, 8, 2));

        var events = await this.Service.BuildEventsAsync([reservation], includeGuest: false);

        Assert.That(events.Single().Uid, Is.EqualTo($"reservation-{reservation.Id}@welcomemat"));
    }

    private static Property MakeProperty(string name, string timeZone) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        TimeZone = timeZone,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static Reservation MakeReservation(
        Property property, DateOnly checkIn, DateOnly checkOut, string notes = "") => new()
        {
            Id = Guid.NewGuid(),
            PropertyId = property.Id,
            Property = property,
            CheckIn = checkIn,
            CheckOut = checkOut,
            Notes = notes,
            CreatedAt = DateTime.UtcNow,
        };
}
