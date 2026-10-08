using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class ReservationCoordinatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.UtcNow;


        private readonly DateTimeOffset UtcNow;
    }

    private class RecordingPushoverService : IPushoverService
    {
        public List<string> Messages { get; } = [];

        public Task SendAsync(string message, string? url = null, string? urlTitle = null)
        {
            lock (this.Messages)
            {
                this.Messages.Add(message);
            }

            return Task.CompletedTask;
        }
    }

    private SqliteTestDatabase Sqlite = null!;
    private Guid PropertyId;
    private Guid Property2Id;
    private Guid GuestId;
    private Guid Guest2Id;
    private Guid AdminId;
    private string GuestEmail = "";
    private string UnlistedEmail = "";
    private Guid UnlistedUserId;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();

        await using var db = this.NewDb();
        var now = DateTime.UtcNow;

        var admin = new User { Id = Guid.NewGuid(), Email = "admin@example.com", Name = "Admin", IsAdmin = true, CreatedAt = now, UpdatedAt = now };
        var guest = new User { Id = Guid.NewGuid(), Email = "guest@example.com", Name = "Guest", CreatedAt = now, UpdatedAt = now };
        var guest2 = new User { Id = Guid.NewGuid(), Email = "guest2@example.com", Name = "Guest Two", CreatedAt = now, UpdatedAt = now };
        var unlisted = new User { Id = Guid.NewGuid(), Email = "unlisted@example.com", Name = "Unlisted", CreatedAt = now, UpdatedAt = now };
        db.Users.AddRange(admin, guest, guest2, unlisted);

        db.Allowlist.AddRange(
            new AllowlistEntry { Id = Guid.NewGuid(), Email = admin.Email, CreatedAt = now },
            new AllowlistEntry { Id = Guid.NewGuid(), Email = guest.Email, CreatedAt = now },
            new AllowlistEntry { Id = Guid.NewGuid(), Email = guest2.Email, CreatedAt = now });
        // No allowlist entry for `unlisted`, on purpose.

        var property = new Property { Id = Guid.NewGuid(), Name = "Beach House", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var property2 = new Property { Id = Guid.NewGuid(), Name = "Mountain Cabin", IsActive = true, CreatedAt = now, UpdatedAt = now };
        var inactiveProperty = new Property { Id = InactivePropertyId, Name = "Closed House", IsActive = false, CreatedAt = now, UpdatedAt = now };
        db.Properties.AddRange(property, property2, inactiveProperty);

        await db.SaveChangesAsync();

        this.AdminId = admin.Id;
        this.GuestId = guest.Id;
        this.Guest2Id = guest2.Id;
        this.GuestEmail = guest.Email;
        this.UnlistedUserId = unlisted.Id;
        this.UnlistedEmail = unlisted.Email;
        this.PropertyId = property.Id;
        this.Property2Id = property2.Id;
    }

    private static readonly Guid InactivePropertyId = Guid.NewGuid();

    [TearDown]
    public void TearDown() => this.Sqlite.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(this.Sqlite.ConnectionString)
        .Options);

    private static PublicUrlService NewPublicUrls() =>
        new(new ValidatedAppConfiguration(new AppOptions()));

    private (ReservationCoordinator Coordinator, AppDbContext Db, RecordingPushoverService Pushover) NewCoordinator()
    {
        var db = this.NewDb();
        var clock = new PropertyClock(new ManualTimeProvider(FixedNow));
        var repo = new EfReservationRepository(db);
        var reservationService = new ReservationService(repo, clock);
        var pushover = new RecordingPushoverService();
        var coordinator = new ReservationCoordinator(db, reservationService, clock, pushover, NewPublicUrls());
        return (coordinator, db, pushover);
    }

    private static ClaimsPrincipal PrincipalFor(Guid userId, string name = "Guest", bool isAdmin = false)
    {
        var identity = new ClaimsIdentity();
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Name, name));
        if (isAdmin)
        {
            identity.AddClaim(new Claim("is_admin", "true"));
        }

        return new ClaimsPrincipal(identity);
    }

    private static ReservationCoordinator.CreateRequest RequestFor(
        string checkIn, string checkOut, Guid? propertyId = null, Guid? targetUserId = null) =>
        new()
        {
            PropertyId = propertyId ?? Guid.Empty,
            CheckIn = DateOnly.ParseExact(checkIn, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            CheckOut = DateOnly.ParseExact(checkOut, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            Notes = "notes",
            TargetUserId = targetUserId,
        };

    private async Task<long> ActiveReservationCount()
    {
        await using var db = this.NewDb();
        return await db.Reservations.LongCountAsync(r => r.DeletedAt == null);
    }

    [Test]
    [Repeat(25)]
    public async Task ConcurrentOverlappingCreates_ExactlyOneSucceeds()
    {
        await using var db1 = this.NewDb();
        await using var db2 = this.NewDb();
        var clock = new PropertyClock(new ManualTimeProvider(FixedNow));
        var pushover1 = new RecordingPushoverService();
        var pushover2 = new RecordingPushoverService();
        var coordinator1 = new ReservationCoordinator(
            db1, new ReservationService(new EfReservationRepository(db1), clock), clock, pushover1, NewPublicUrls());
        var coordinator2 = new ReservationCoordinator(
            db2, new ReservationService(new EfReservationRepository(db2), clock), clock, pushover2, NewPublicUrls());

        var actor1 = PrincipalFor(this.GuestId, "Guest One");
        var actor2 = PrincipalFor(this.Guest2Id, "Guest Two");

        using var barrier = new Barrier(2);

        Task<ReservationMutationResult> Race(ReservationCoordinator coordinator, ReservationCoordinator.CreateRequest request, ClaimsPrincipal actor) =>
            Task.Run(async () =>
            {
                barrier.SignalAndWait();
                return await coordinator.CreateAsync(request, actor, bypassValidation: false, default);
            });

        var results = await Task.WhenAll(
            Race(coordinator1, RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor1),
            Race(coordinator2, RequestFor("2026-09-02", "2026-09-04", this.PropertyId), actor2));

        Assert.That(results.Count(r => r.Status == ReservationMutationStatus.Success), Is.EqualTo(1));
        Assert.That(results.Count(r => r.Status == ReservationMutationStatus.Conflict), Is.EqualTo(1));
        Assert.That(await this.ActiveReservationCount(), Is.EqualTo(1));
    }

    [Test]
    public async Task SameDatesAtTwoDifferentProperties_BothSucceed()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId, "Guest One");

        var result1 = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor, false, default);
        var result2 = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.Property2Id), actor, false, default);

        Assert.That(result1.Status, Is.EqualTo(ReservationMutationStatus.Success));
        Assert.That(result2.Status, Is.EqualTo(ReservationMutationStatus.Success));
        Assert.That(await this.ActiveReservationCount(), Is.EqualTo(2));
    }

    [Test]
    public async Task InactiveProperty_ReturnsValidationError()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var result = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", InactivePropertyId), actor, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
        Assert.That(result.Error, Does.Contain("inactive").IgnoreCase);
    }

    [Test]
    public async Task MissingProperty_ReturnsValidationError()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var result = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", Guid.NewGuid()), actor, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
    }

    [Test]
    public async Task NonexistentTargetUser_ReturnsValidationError()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var admin = PrincipalFor(this.AdminId, "Admin", isAdmin: true);

        var result = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId, targetUserId: Guid.NewGuid()), admin, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
        Assert.That(result.Error, Does.Contain("not found"));
    }

    [Test]
    public async Task NonAllowlistedTargetUser_ReturnsValidationError()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var admin = PrincipalFor(this.AdminId, "Admin", isAdmin: true);

        var result = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId, targetUserId: this.UnlistedUserId), admin, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
        Assert.That(result.Error, Does.Contain("allowlist"));
    }

    [Test]
    public async Task BookingWindowExceeded_ReturnsValidationError()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        // Well past the 90-day default horizon.
        var result = await coordinator.CreateAsync(
            RequestFor("2027-06-01", "2027-06-05", this.PropertyId), actor, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
        Assert.That(result.Error, Does.Contain("advance"));
    }

    [Test]
    public async Task BypassValidation_AllowsPastDateThatNormalValidationWouldReject()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        // A past check-in, which the normal path refuses outright.
        var blocked = await coordinator.CreateAsync(
            RequestFor("2026-08-01", "2026-08-05", this.PropertyId), actor, bypassValidation: false, default);
        Assert.That(blocked.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));

        var bypassed = await coordinator.CreateAsync(
            RequestFor("2026-08-01", "2026-08-05", this.PropertyId), actor, bypassValidation: true, default);

        Assert.That(bypassed.Status, Is.EqualTo(ReservationMutationStatus.Success));
        Assert.That(bypassed.Reservation!.CheckIn, Is.EqualTo(DateOnly.ParseExact("2026-08-01", "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(await this.ActiveReservationCount(), Is.EqualTo(1));
    }

    [Test]
    public async Task BypassValidation_AllowsOverlapThatNormalValidationWouldReject()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var first = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor, bypassValidation: true, default);
        Assert.That(first.Status, Is.EqualTo(ReservationMutationStatus.Success));

        // A flat overlap, which the normal path would call a Conflict.
        var second = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor, bypassValidation: true, default);

        Assert.That(second.Status, Is.EqualTo(ReservationMutationStatus.Success));
        Assert.That(await this.ActiveReservationCount(), Is.EqualTo(2));
    }

    [Test]
    public async Task BypassValidation_StillRejectsInactiveProperty()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var result = await coordinator.CreateAsync(
            RequestFor("2026-08-01", "2026-08-05", InactivePropertyId), actor, bypassValidation: true, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.ValidationError));
        Assert.That(result.Error, Does.Contain("inactive").IgnoreCase);
    }

    [Test]
    public async Task OverlappingReservation_OutsideConcurrency_MapsToConflict()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var first = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor, false, default);
        Assert.That(first.Status, Is.EqualTo(ReservationMutationStatus.Success));

        var second = await coordinator.CreateAsync(
            RequestFor("2026-09-02", "2026-09-04", this.PropertyId), actor, false, default);

        Assert.That(second.Status, Is.EqualTo(ReservationMutationStatus.Conflict));
        Assert.That(second.Error, Is.EqualTo(ReservationMutationResult.ConflictMessage));
    }

    [Test]
    public async Task Cancel_UnknownReservation_ReturnsNotFound()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId);

        var result = await coordinator.CancelAsync(Guid.NewGuid(), actor, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.NotFound));
    }

    [Test]
    public async Task Cancel_OtherUsersReservation_ReturnsForbidden()
    {
        var (coordinator, _, _) = this.NewCoordinator();
        var owner = PrincipalFor(this.GuestId);
        var created = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), owner, false, default);

        var stranger = PrincipalFor(this.Guest2Id);
        var result = await coordinator.CancelAsync(created.Reservation!.Id, stranger, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.Forbidden));
    }

    [Test]
    public async Task Create_SendsPushoverNotification_AfterCommit()
    {
        var (coordinator, _, pushover) = this.NewCoordinator();
        var actor = PrincipalFor(this.GuestId, "Guest One");

        var result = await coordinator.CreateAsync(
            RequestFor("2026-09-01", "2026-09-05", this.PropertyId), actor, false, default);

        Assert.That(result.Status, Is.EqualTo(ReservationMutationStatus.Success));
        Assert.That(pushover.Messages, Has.Count.EqualTo(1));
        Assert.That(pushover.Messages[0], Does.Contain("Guest One"));
    }
}
