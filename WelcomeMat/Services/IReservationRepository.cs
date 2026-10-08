using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An interface representing the reservation reads the booking rules need.
/// </summary>
public interface IReservationRepository
{
    /// <summary>
    /// Fetch the application settings.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<List<Setting>> GetSettingsAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch a user's live stays at a property, optionally ignoring one.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="userId"></param>
    /// <param name="excludeId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<List<(DateOnly CheckIn, DateOnly CheckOut)>> GetActiveReservationsForUserAsync(
        Guid propertyId, Guid userId, Guid? excludeId, CancellationToken ct = default);

    /// <summary>
    /// Determine if the span collides with an existing stay.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="propertyId"></param>
    /// <param name="excludeId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<bool> HasOverlapAsync(
        DateOnly checkIn, DateOnly checkOut, Guid propertyId, Guid? excludeId,
        CancellationToken ct = default);

    /// <summary>
    /// Fetch the first blacked-out date in the span, if any.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="propertyId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    Task<DateOnly?> GetFirstBlackoutAsync(
        DateOnly checkIn, DateOnly checkOut, Guid propertyId,
        CancellationToken ct = default);
}
