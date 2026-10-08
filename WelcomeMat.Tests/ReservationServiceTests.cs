using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class ReservationServiceTests
{

    private static Reservation Stay(DateOnly checkIn, DateOnly checkOut) =>
        new() { Id = Guid.NewGuid(), CheckIn = checkIn, CheckOut = checkOut };

    [Test]
    public void SelectCurrentOrNext_CheckOutToday_IsStillShown()
    {
        var today = new DateOnly(2026, 6, 28);
        var current = Stay(today.AddDays(-2), today); // checks out today

        var result = ReservationService.SelectCurrentOrNext([current], today);

        Assert.That(result, Is.SameAs(current));
    }

    [Test]
    public void SelectCurrentOrNext_CheckOutYesterday_IsNotShown()
    {
        var today = new DateOnly(2026, 6, 28);
        var past = Stay(today.AddDays(-3), today.AddDays(-1)); // checked out yesterday

        var result = ReservationService.SelectCurrentOrNext([past], today);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void SelectCurrentOrNext_CurrentStayPreferredOverFutureStay()
    {
        var today = new DateOnly(2026, 6, 28);
        var current = Stay(today.AddDays(-2), today);          // ends today
        var future = Stay(today.AddDays(10), today.AddDays(13));

        var result = ReservationService.SelectCurrentOrNext([future, current], today);

        Assert.That(result, Is.SameAs(current));
    }

    [Test]
    public void SelectCurrentOrNext_OnlyFutureStay_ReturnsThatStay()
    {
        var today = new DateOnly(2026, 6, 28);
        var future = Stay(today.AddDays(10), today.AddDays(13));

        var result = ReservationService.SelectCurrentOrNext([future], today);

        Assert.That(result, Is.SameAs(future));
    }

    [Test]
    public void SelectCurrentOrNext_NoReservations_ReturnsNull()
    {
        var today = new DateOnly(2026, 6, 28);

        var result = ReservationService.SelectCurrentOrNext([], today);

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetSettings_EmptyTable_ReturnsDefaults()
    {
        var repo = DefaultRepo(settings: []);
        var service = new ReservationService(repo.Object, Clock);

        var s = await service.GetSettingsAsync(TestProperty);

        Assert.That(s.MaxConsecutiveNights, Is.EqualTo(7));
        Assert.That(s.MinAdvanceNoticeDays, Is.EqualTo(1));
        Assert.That(s.MaxAdvanceBookingDays, Is.EqualTo(90));
    }

    [Test]
    public async Task GetSettings_PartialTable_MissingKeysUseDefaults()
    {
        var repo = DefaultRepo(settings:
        [
            new Setting { Key = "max_consecutive_nights", Value = "14" },
        ]);
        var service = new ReservationService(repo.Object, Clock);

        var s = await service.GetSettingsAsync(TestProperty);

        Assert.That(s.MaxConsecutiveNights, Is.EqualTo(14));
        Assert.That(s.MinAdvanceNoticeDays, Is.EqualTo(1));   // default
        Assert.That(s.MaxAdvanceBookingDays, Is.EqualTo(90));  // default
    }

    [Test]
    public async Task GetSettings_FullTable_UsesStoredValues()
    {
        var repo = DefaultRepo(settings:
        [
            new Setting { Key = "max_consecutive_nights", Value = "5" },
            new Setting { Key = "min_advance_notice_days", Value = "3" },
            new Setting { Key = "max_advance_booking_days", Value = "60" },
        ]);
        var service = new ReservationService(repo.Object, Clock);

        var s = await service.GetSettingsAsync(TestProperty);

        Assert.That(s.MaxConsecutiveNights, Is.EqualTo(5));
        Assert.That(s.MinAdvanceNoticeDays, Is.EqualTo(3));
        Assert.That(s.MaxAdvanceBookingDays, Is.EqualTo(60));
    }

    private static readonly PropertyClock Clock = new(TimeProvider.System);
    private static DateOnly RealToday => Clock.Today("UTC");
    private static DateOnly FutureCheckIn => RealToday.AddDays(10);
    private static DateOnly FutureCheckOut => FutureCheckIn.AddDays(3);
    private static readonly Property TestProperty = new() { Id = Guid.NewGuid(), TimeZone = "UTC" };
    private static Guid PropertyId => TestProperty.Id;
    private static readonly Guid UserId = Guid.NewGuid();

    [Test]
    public async Task Validate_CheckInAfterCheckOut_ReturnsInvalid()
    {
        var service = new ReservationService(DefaultRepo().Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckOut, FutureCheckIn, TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task Validate_CheckInInPast_ReturnsInvalid()
    {
        var past = RealToday.AddDays(-5);
        var service = new ReservationService(DefaultRepo().Object, Clock);

        var result = await service.ValidateAsync(
            past, past.AddDays(2), TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("past"));
    }

    [Test]
    public async Task Validate_AdvanceNoticeViolated_ReturnsInvalid()
    {
        // Two days out against a five-day notice requirement.
        var repo = DefaultRepo(minAdvanceDays: 5);
        var checkIn = RealToday.AddDays(2);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            checkIn, checkIn.AddDays(2), TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("advance notice"));
    }

    [Test]
    public async Task Validate_AdvanceNoticeViolated_Admin_ReturnsValid()
    {
        var repo = DefaultRepo(minAdvanceDays: 5);
        var checkIn = RealToday.AddDays(2);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            checkIn, checkIn.AddDays(2), TestProperty, UserId, isAdmin: true);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task Validate_BookingWindowExceeded_ReturnsInvalid()
    {
        // Twice the booking horizon.
        var repo = DefaultRepo(maxBookingDays: 30);
        var checkIn = RealToday.AddDays(60);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            checkIn, checkIn.AddDays(2), TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("days in advance"));
    }

    [Test]
    public async Task Validate_BookingWindowExceeded_Admin_ReturnsValid()
    {
        var repo = DefaultRepo(maxBookingDays: 30);
        var checkIn = RealToday.AddDays(60);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            checkIn, checkIn.AddDays(2), TestProperty, UserId, isAdmin: true);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task Validate_MaxNightsExceeded_ReturnsInvalid()
    {
        // Five nights against a three-night cap.
        var repo = DefaultRepo(maxNights: 3);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckIn.AddDays(5), TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("consecutive nights"));
    }

    [Test]
    public async Task Validate_MaxNightsExceeded_Admin_StillReturnsInvalid()
    {
        var repo = DefaultRepo(maxNights: 3);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckIn.AddDays(5), TestProperty, UserId, isAdmin: true);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task Validate_OverlappingReservation_ReturnsInvalid()
    {
        var repo = DefaultRepo();
        repo.Setup(r => r.HasOverlapAsync(
                FutureCheckIn, FutureCheckOut, PropertyId, null, default))
            .ReturnsAsync(true);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("overlap"));
    }

    [Test]
    public async Task Validate_OverlappingReservation_Admin_StillReturnsInvalid()
    {
        var repo = DefaultRepo();
        repo.Setup(r => r.HasOverlapAsync(
                FutureCheckIn, FutureCheckOut, PropertyId, null, default))
            .ReturnsAsync(true);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId, isAdmin: true);

        Assert.That(result.IsValid, Is.False);
    }

    [Test]
    public async Task Validate_BlackoutConflict_ReturnsInvalid()
    {
        var repo = DefaultRepo();
        repo.Setup(r => r.GetFirstBlackoutAsync(
                FutureCheckIn, FutureCheckOut, PropertyId, default))
            .ReturnsAsync(FutureCheckIn);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("blackout"));
    }

    [Test]
    public async Task Validate_BlackoutConflict_Admin_ReturnsValid()
    {
        var repo = DefaultRepo();
        repo.Setup(r => r.GetFirstBlackoutAsync(
                FutureCheckIn, FutureCheckOut, PropertyId, default))
            .ReturnsAsync(FutureCheckIn);
        var service = new ReservationService(repo.Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId, isAdmin: true);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task Validate_ValidReservation_ReturnsValid()
    {
        var service = new ReservationService(DefaultRepo().Object, Clock);

        var result = await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.True);
    }

    [Test]
    public async Task Validate_ExcludeReservationId_PassedToRepo()
    {
        var excludeId = Guid.NewGuid();
        var repo = DefaultRepo();
        var service = new ReservationService(repo.Object, Clock);

        await service.ValidateAsync(
            FutureCheckIn, FutureCheckOut, TestProperty, UserId,
            isAdmin: false, excludeReservationId: excludeId);

        repo.Verify(r => r.HasOverlapAsync(
            FutureCheckIn, FutureCheckOut, PropertyId, excludeId, default), Times.Once);
    }

    [Test]
    public async Task Validate_UsesPropertysOwnTimeZone_NotUtc()
    {
        var fixedClock = new PropertyClock(new FixedTimeProvider(
            new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero)));
        var futureZoneProperty = new Property { Id = Guid.NewGuid(), TimeZone = "Pacific/Kiritimati" };
        var utcToday = fixedClock.Today("UTC");
        var localToday = fixedClock.Today("Pacific/Kiritimati");
        Assert.That(localToday, Is.EqualTo(utcToday.AddDays(1)));

        var service = new ReservationService(DefaultRepo().Object, fixedClock);

        var result = await service.ValidateAsync(
            utcToday, utcToday.AddDays(2), futureZoneProperty, UserId, isAdmin: false);

        Assert.That(result.IsValid, Is.False);
        Assert.That(result.Error, Does.Contain("past"));
    }

    private class FixedTimeProvider : TimeProvider
    {
        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.UtcNow;


        private readonly DateTimeOffset UtcNow;
    }

    private static Mock<IReservationRepository> DefaultRepo(
        List<Setting>? settings = null,
        int maxNights = 7,
        int minAdvanceDays = 1,
        int maxBookingDays = 90)
    {
        settings ??=
        [
            new Setting { Key = "max_consecutive_nights", Value = maxNights.ToString() },
            new Setting { Key = "min_advance_notice_days", Value = minAdvanceDays.ToString() },
            new Setting { Key = "max_advance_booking_days", Value = maxBookingDays.ToString() },
        ];

        var mock = new Mock<IReservationRepository>();
        mock.Setup(r => r.GetSettingsAsync(default)).ReturnsAsync(settings);
        mock.Setup(r => r.GetActiveReservationsForUserAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), default))
            .ReturnsAsync([]);
        mock.Setup(r => r.HasOverlapAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(), It.IsAny<Guid?>(), default))
            .ReturnsAsync(false);
        mock.Setup(r => r.GetFirstBlackoutAsync(
                It.IsAny<DateOnly>(), It.IsAny<DateOnly>(), It.IsAny<Guid>(), default))
            .ReturnsAsync((DateOnly?)null);
        return mock;
    }
}
