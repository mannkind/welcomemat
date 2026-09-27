using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class PropertyAccessServiceTests
{
    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;
    private User Guest = null!;
    private User Admin = null!;
    private User OtherGuest = null!;
    private Property Property = null!;

    private class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.UtcNow;


        private readonly DateTimeOffset UtcNow;
    }

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .Options);

        var now = DateTime.UtcNow;
        this.Guest = new User { Id = Guid.NewGuid(), Email = "guest@example.com", Name = "Guest", CreatedAt = now, UpdatedAt = now };
        this.Admin = new User { Id = Guid.NewGuid(), Email = "admin@example.com", Name = "Admin", IsAdmin = true, CreatedAt = now, UpdatedAt = now };
        this.OtherGuest = new User { Id = Guid.NewGuid(), Email = "other@example.com", Name = "Other", CreatedAt = now, UpdatedAt = now };
        this.Db.Users.AddRange(this.Guest, this.Admin, this.OtherGuest);

        this.Property = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Test House",
            WifiNetwork = "TestWifi",
            WifiPassword = "wifipass",
            DoorCode = "0000",
            CheckinProcedures = "Use the lockbox",
            AccessWindowDays = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        this.Db.Properties.Add(this.Property);

        this.Db.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = this.Guest.Id,
            PropertyId = this.Property.Id,
            CheckIn = new DateOnly(2026, 8, 10),
            CheckOut = new DateOnly(2026, 8, 12),
            CreatedAt = now,
        });

        await this.Db.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    private PropertyAccessService CreateService(DateTimeOffset utcNow) =>
        new(this.Db, new PropertyClock(new ManualTimeProvider(utcNow)), new StayTimesService(this.Db));

    private static ClaimsPrincipal PrincipalFor(User user) => AuthClaims.BuildPrincipal(user);

    [TestCase("2026-08-09T21:59:59Z", false)]
    [TestCase("2026-08-09T22:00:00Z", true)]
    [TestCase("2026-08-12T17:59:59Z", true)]
    [TestCase("2026-08-12T18:00:00Z", false)]
    public async Task SecretWindow_UsesConfiguredStayTimes(string utc, bool allowed)
    {
        var service = this.CreateService(DateTimeOffset.Parse(utc));

        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result is not null, Is.EqualTo(allowed));
    }

    [TestCase("2026-08-12T17:59:59Z", true)]
    [TestCase("2026-08-12T18:00:00Z", false)]
    public async Task SecretWindow_EndsExactlyWhenTheCalendarFeedSaysTheStayEnds(string utc, bool allowed)
    {
        var calendar = new ReservationCalendarService(
            new StayTimesService(this.Db), new PropertyClock(TimeProvider.System));
        var reservation = await this.Db.Reservations.FirstAsync(r => r.UserId == this.Guest.Id);
        reservation.Property = this.Property;
        var events = await calendar.BuildEventsAsync([reservation], includeGuest: false);

        var service = this.CreateService(DateTimeOffset.Parse(utc));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.Multiple(() =>
        {
            Assert.That(events.Single().End, Is.EqualTo(
                new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero)));
            Assert.That(result is not null, Is.EqualTo(allowed));
        });
    }

    [TestCase("2026-08-12T15:59:59Z", true)]
    [TestCase("2026-08-12T16:00:00Z", false)]
    public async Task SecretWindow_HonoursAPerPropertyCheckOutTimeOverride(string utc, bool allowed)
    {
        this.Property.CheckOutTime = new TimeOnly(9, 0);
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(DateTimeOffset.Parse(utc));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result is not null, Is.EqualTo(allowed));
    }

    [Test]
    public async Task Admin_AlwaysHasAccess_EvenOutsideAnyReservationWindow()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetAsync(PrincipalFor(this.Admin), this.Property.Id, default);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task Admin_PersonalDoorCode_OverridesPropertyDoorCode_WhenNonEmpty()
    {
        this.Db.GuestDoorCodes.Add(new GuestDoorCode { UserId = this.Admin.Id, PropertyId = this.Property.Id, DoorCode = "5150" });
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Admin), this.Property.Id, default);

        Assert.That(result!.EffectiveDoorCode, Is.EqualTo("5150"));
    }

    [Test]
    public async Task OtherUser_HasNoAccess_DuringGuestsWindow()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetAsync(PrincipalFor(this.OtherGuest), this.Property.Id, default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task DeletedReservation_GrantsNoAccess()
    {
        var reservation = await this.Db.Reservations.FirstAsync(r => r.UserId == this.Guest.Id);
        reservation.DeletedAt = DateTime.UtcNow;
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task BackToBackStays_KeepAccessContinuousAcrossTheBoundary()
    {
        this.Db.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = this.Guest.Id,
            PropertyId = this.Property.Id,
            CheckIn = new DateOnly(2026, 8, 12),
            CheckOut = new DateOnly(2026, 8, 14),
            CreatedAt = DateTime.UtcNow,
        });
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 8, 12, 18, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task CustomGuestDoorCode_OverridesPropertyDoorCode_WhenNonEmpty()
    {
        this.Db.GuestDoorCodes.Add(new GuestDoorCode { UserId = this.Guest.Id, PropertyId = this.Property.Id, DoorCode = "9999" });
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result!.EffectiveDoorCode, Is.EqualTo("9999"));
    }

    [Test]
    public async Task EmptyCustomGuestDoorCode_FallsBackToPropertyDoorCode()
    {
        this.Db.GuestDoorCodes.Add(new GuestDoorCode { UserId = this.Guest.Id, PropertyId = this.Property.Id, DoorCode = "" });
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result!.EffectiveDoorCode, Is.EqualTo(this.Property.DoorCode));
    }

    [Test]
    public async Task MissingCustomGuestDoorCode_FallsBackToPropertyDoorCode_EvenWhenEmpty()
    {
        this.Property.DoorCode = "";
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result!.EffectiveDoorCode, Is.Empty);
    }

    [Test]
    public async Task DeniedRequest_NeverSelectsSecretColumns()
    {
        var interceptedCommands = new List<string>();
        await using var interceptedDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .AddInterceptors(new CommandTextCapturingInterceptor(interceptedCommands))
            .Options);

        var service = new PropertyAccessService(
            interceptedDb,
            new PropertyClock(new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))),
            new StayTimesService(interceptedDb));

        var result = await service.GetAsync(PrincipalFor(this.OtherGuest), this.Property.Id, default);

        Assert.That(result, Is.Null);
        Assert.Multiple(() =>
        {
            foreach (var column in new[] { "door_code", "wifi_network", "wifi_password", "checkin_procedures" })
            {
                Assert.That(
                    interceptedCommands.Any(c => c.Contains(column, StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    $"A denied request must never select {column}.");
            }
        });
    }

    [Test]
    public async Task AnonymousOrUnparseableUserId_IsDenied()
    {
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);

        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(principal, this.Property.Id, default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task UnknownProperty_ReturnsNull_ForAdmin()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 8, 11, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetAsync(PrincipalFor(this.Admin), Guid.NewGuid(), default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetCheckinProceduresAsync_VisibleToGuest_OutsideThePhysicalSecretWindow()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetCheckinProceduresAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result, Is.EqualTo("Use the lockbox"));
    }

    [Test]
    public async Task GetCheckinProceduresAsync_DeniedToUserWithNoReservation()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetCheckinProceduresAsync(PrincipalFor(this.OtherGuest), this.Property.Id, default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetCheckinProceduresAsync_DeniedForDeletedReservation()
    {
        var reservation = await this.Db.Reservations.FirstAsync(r => r.UserId == this.Guest.Id);
        reservation.DeletedAt = DateTime.UtcNow;
        await this.Db.SaveChangesAsync();

        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var result = await service.GetCheckinProceduresAsync(PrincipalFor(this.Guest), this.Property.Id, default);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetCheckinProceduresAsync_VisibleToAdmin_EvenWithoutReservation()
    {
        var service = this.CreateService(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var result = await service.GetCheckinProceduresAsync(PrincipalFor(this.Admin), this.Property.Id, default);

        Assert.That(result, Is.EqualTo("Use the lockbox"));
    }
}

file class CommandTextCapturingInterceptor : DbCommandInterceptor
{
    public CommandTextCapturingInterceptor(List<string> sink)
    {
        this.Sink = sink;
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        this.Sink.Add(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        this.Sink.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    private readonly List<string> Sink;
}
