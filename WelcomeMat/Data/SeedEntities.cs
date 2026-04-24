using System;
using System.Globalization;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Data;

/// <summary>
/// The rows the dev and demo seeders both build; only their data differs.
/// </summary>
internal static class SeedEntities
{
    /// <summary>
    /// Build a favorite spot.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="name"></param>
    /// <param name="address"></param>
    /// <param name="description"></param>
    /// <param name="lat"></param>
    /// <param name="lon"></param>
    /// <param name="sortOrder"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static FavoriteSpot Spot(
        Guid propertyId, string name, string address, string description,
        double lat, double lon, int sortOrder, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Name = name,
            Address = address,
            Description = description,
            Latitude = lat,
            Longitude = lon,
            SortOrder = sortOrder,
            CreatedAt = now,
        };

    /// <summary>
    /// Build a blackout date.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="date"></param>
    /// <param name="reason"></param>
    /// <param name="createdBy"></param>
    /// <param name="now"></param>
    /// <returns></returns>
    public static BlackoutDate Blackout(
        Guid propertyId, DateOnly date, string reason, Guid createdBy, DateTime now) => new()
        {
            Id = Guid.NewGuid(),
            PropertyId = propertyId,
            Date = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
}
