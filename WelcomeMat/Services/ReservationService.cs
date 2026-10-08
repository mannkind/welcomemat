using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// The booking rules in force for a property, after the global defaults are
/// layered under its overrides.
/// </summary>
public record StaySettings
{
    public int MaxConsecutiveNights { get; init; }

    public int MinAdvanceNoticeDays { get; init; }

    public int MaxAdvanceBookingDays { get; init; }
}

/// <summary>
/// Whether a stay is bookable, and why not.
/// </summary>
public record ValidationResult
{
    public bool IsValid { get; init; }

    public string? Error { get; init; } = null;

    /// <summary>
    /// The stay is bookable.
    /// </summary>
    /// <returns></returns>
    public static ValidationResult Ok() => new() { IsValid = true };

    /// <summary>
    /// The stay is not bookable, and why.
    /// </summary>
    /// <param name="error"></param>
    /// <returns></returns>
    public static ValidationResult Fail(string error) => new() { IsValid = false, Error = error };
}

/// <summary>
/// An class representing the booking rules.
/// </summary>
public class ReservationService
{
    /// <summary>
    /// Initializes a new instance of the ReservationService class.
    /// </summary>
    /// <param name="repo"></param>
    /// <param name="clock"></param>
    public ReservationService(IReservationRepository repo, PropertyClock clock)
    {
        this.Repo = repo;
        this.Clock = clock;
    }

    public const string OverlapErrorMessage = "Those dates overlap with an existing reservation.";

    /// <summary>
    /// The booking rules for a property, its overrides over the global defaults.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<StaySettings> GetSettingsAsync(Property property, CancellationToken ct = default)
    {
        var settings = (await this.Repo.GetSettingsAsync(ct)).ToDictionary(s => s.Key, s => s.Value);
        return new StaySettings
        {
            MaxConsecutiveNights = property.MaxConsecutiveNights ?? GetInt(settings, "max_consecutive_nights", 7),
            MinAdvanceNoticeDays = property.MinAdvanceNoticeDays ?? GetInt(settings, "min_advance_notice_days", 1),
            MaxAdvanceBookingDays = property.MaxAdvanceBookingDays ?? GetInt(settings, "max_advance_booking_days", 90),
        };
    }

    private static int GetInt(Dictionary<string, string> d, string key, int fallback) =>
        d.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    /// <summary>
    /// Check a stay against the booking rules.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="property"></param>
    /// <param name="userId"></param>
    /// <param name="isAdmin"></param>
    /// <param name="excludeReservationId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<ValidationResult> ValidateAsync(
        DateOnly checkIn,
        DateOnly checkOut,
        Property property,
        Guid userId,
        bool isAdmin,
        Guid? excludeReservationId = null,
        CancellationToken ct = default)
    {
        if (checkOut <= checkIn)
        {
            return ValidationResult.Fail("Check-out must be after check-in.");
        }

        var settings = await this.GetSettingsAsync(property, ct);
        var today = this.Clock.Today(property.TimeZone);

        var totalNights = await this.CountConsecutiveNightsAsync(checkIn, checkOut, property.Id, userId, excludeReservationId, ct);
        if (totalNights > settings.MaxConsecutiveNights)
        {
            return ValidationResult.Fail($"Maximum {settings.MaxConsecutiveNights} consecutive nights allowed.");
        }

        if (!isAdmin)
        {
            if (checkIn < today)
            {
                return ValidationResult.Fail("Check-in date cannot be in the past.");
            }

            var daysAhead = checkIn.DayNumber - today.DayNumber;
            if (daysAhead < settings.MinAdvanceNoticeDays)
            {
                return ValidationResult.Fail($"Reservations require at least {settings.MinAdvanceNoticeDays} day(s) advance notice.");
            }

            if (daysAhead > settings.MaxAdvanceBookingDays)
            {
                return ValidationResult.Fail($"Cannot book more than {settings.MaxAdvanceBookingDays} days in advance.");
            }

            var blackout = await this.Repo.GetFirstBlackoutAsync(checkIn, checkOut, property.Id, ct);
            if (blackout is { } bd)
            {
                return ValidationResult.Fail($"{bd:MMM d} is a blackout date.");
            }
        }

        // Last, because it's the only check that reads every other user's bookings.
        var overlap = await this.Repo.HasOverlapAsync(checkIn, checkOut, property.Id, excludeReservationId, ct);
        if (overlap)
        {
            return ValidationResult.Fail(OverlapErrorMessage);
        }

        return ValidationResult.Ok();
    }

    /// <summary>
    /// The length of the run this stay would join, counting the guest's stays
    /// that butt up against it.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="propertyId"></param>
    /// <param name="userId"></param>
    /// <param name="excludeId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task<int> CountConsecutiveNightsAsync(
        DateOnly checkIn, DateOnly checkOut,
        Guid propertyId, Guid userId,
        Guid? excludeId,
        CancellationToken ct)
    {
        var adjacent = await this.Repo.GetActiveReservationsForUserAsync(propertyId, userId, excludeId, ct);

        var start = checkIn;
        var end = checkOut;
        bool changed;
        do
        {
            changed = false;
            foreach (var r in adjacent)
            {
                if (r.CheckOut == start) { start = r.CheckIn; changed = true; }
                if (r.CheckIn == end) { end = r.CheckOut; changed = true; }
            }
        } while (changed);

        return end.DayNumber - start.DayNumber;
    }

    /// <summary>
    /// The stay a guest is on, or the one coming up.
    /// </summary>
    /// <param name="reservations"></param>
    /// <param name="today"></param>
    /// <returns></returns>
    public static Reservation? SelectCurrentOrNext(IEnumerable<Reservation> reservations, DateOnly today) =>
        reservations
            .Where(r => r.CheckOut >= today)
            .OrderBy(r => r.CheckIn)
            .FirstOrDefault();

    /// <summary>
    /// The reservation reads used internally.
    /// </summary>
    private readonly IReservationRepository Repo;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;
}
