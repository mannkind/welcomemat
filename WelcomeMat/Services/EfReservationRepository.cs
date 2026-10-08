using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing the reservation reads, over EF Core.
/// </summary>
public class EfReservationRepository : IReservationRepository
{
    /// <summary>
    /// Initializes a new instance of the EfReservationRepository class.
    /// </summary>
    /// <param name="db"></param>
    public EfReservationRepository(AppDbContext db)
    {
        this.Db = db;
    }

    /// <inheritdoc />
    public Task<List<Setting>> GetSettingsAsync(CancellationToken ct = default) =>
        this.Db.Settings.ToListAsync(ct);

    /// <inheritdoc />
    public async Task<List<(DateOnly CheckIn, DateOnly CheckOut)>> GetActiveReservationsForUserAsync(
        Guid propertyId, Guid userId, Guid? excludeId, CancellationToken ct = default)
    {
        var rows = await this.Db.Reservations
            .Where(r => r.PropertyId == propertyId
                && r.UserId == userId
                && r.DeletedAt == null
                && (excludeId == null || r.Id != excludeId))
            .Select(r => new { r.CheckIn, r.CheckOut })
            .ToListAsync(ct);

        return rows.Select(r => (r.CheckIn, r.CheckOut)).ToList();
    }

    /// <inheritdoc />
    public Task<bool> HasOverlapAsync(
        DateOnly checkIn, DateOnly checkOut, Guid propertyId, Guid? excludeId,
        CancellationToken ct = default) =>
        this.Db.Reservations
            .Where(r => r.PropertyId == propertyId
                && r.DeletedAt == null
                && (excludeId == null || r.Id != excludeId)
                && r.CheckIn < checkOut
                && r.CheckOut > checkIn)
            .AnyAsync(ct);

    /// <inheritdoc />
    public async Task<DateOnly?> GetFirstBlackoutAsync(
        DateOnly checkIn, DateOnly checkOut, Guid propertyId,
        CancellationToken ct = default)
    {
        var blackouts = await this.Db.BlackoutDates
            .Where(b => b.PropertyId == propertyId || b.PropertyId == null)
            .ToListAsync(ct);

        foreach (var b in blackouts)
        {
            if (!DateOnly.TryParseExact(b.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bd))
            {
                continue;
            }

            if (bd >= checkIn && bd < checkOut)
            {
                return bd;
            }
        }
        return null;
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
