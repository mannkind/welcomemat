using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;

namespace WelcomeMat.Services;

/// <summary>
/// The wifi and door code details, once a guest is close enough to their stay to
/// see them.
/// </summary>
public record PhysicalAccessInfo
{
    public string WifiNetwork { get; init; } = string.Empty;

    public string WifiPassword { get; init; } = string.Empty;

    public string EffectiveDoorCode { get; init; } = string.Empty;
}

/// <summary>
/// An class representing a managed way to decide which property details a guest
/// may see, and when.
/// </summary>
public class PropertyAccessService
{
    /// <summary>
    /// Initializes a new instance of the PropertyAccessService class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="clock"></param>
    /// <param name="stayTimes"></param>
    public PropertyAccessService(AppDbContext db, PropertyClock clock, StayTimesService stayTimes)
    {
        this.Db = db;
        this.Clock = clock;
        this.StayTimes = stayTimes;
    }

    /// <summary>
    /// The wifi and door code for a guest, once their stay is close enough;
    /// null when it is not.
    /// </summary>
    /// <param name="principal"></param>
    /// <param name="propertyId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<PhysicalAccessInfo?> GetAsync(
        ClaimsPrincipal principal, Guid propertyId, CancellationToken ct = default)
    {
        var isAdmin = principal.HasClaim("is_admin", "true");

        if (!isAdmin)
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }

            var candidates = await this.Db.Reservations
                .Where(r => r.PropertyId == propertyId && r.UserId == userId && r.DeletedAt == null)
                .Select(r => new { r.CheckIn, r.CheckOut })
                .ToListAsync(ct);

            var propertyMeta = await this.Db.Properties
                .Where(p => p.Id == propertyId)
                .Select(p => new { p.TimeZone, p.AccessWindowDays, p.CheckInTime, p.CheckOutTime })
                .FirstOrDefaultAsync(ct);
            if (propertyMeta is null)
            {
                return null;
            }

            var timeZone = propertyMeta.TimeZone;

            var accessWindowDays = propertyMeta.AccessWindowDays ?? await this.GetGlobalAccessWindowDaysAsync(ct);

            var times = StayTimesService.Resolve(
                propertyMeta.CheckInTime, propertyMeta.CheckOutTime, await this.StayTimes.GetGlobalAsync(ct));

            var now = this.Clock.GetLocalNow(timeZone);
            var authorized = candidates.Any(r =>
                now >= this.Clock.AtBoundary(r.CheckIn, timeZone, times.CheckIn).AddDays(-accessWindowDays) &&
                now < this.Clock.AtBoundary(r.CheckOut, timeZone, times.CheckOut));

            if (!authorized)
            {
                return null;
            }

            var customDoorCode = await this.Db.GuestDoorCodes
                .Where(g => g.UserId == userId && g.PropertyId == propertyId)
                .Select(g => (string?)g.DoorCode)
                .FirstOrDefaultAsync(ct);

            var secrets = await this.LoadSecretsAsync(propertyId, ct);
            if (secrets is null)
            {
                return null;
            }

            var effectiveDoorCode = string.IsNullOrEmpty(customDoorCode) ? secrets.DoorCode : customDoorCode;
            return new PhysicalAccessInfo
            {
                WifiNetwork = secrets.WifiNetwork,
                WifiPassword = secrets.WifiPassword,
                EffectiveDoorCode = effectiveDoorCode,
            };
        }

        var adminSecrets = await this.LoadSecretsAsync(propertyId, ct);
        if (adminSecrets is null)
        {
            return null;
        }

        string? adminCustomDoorCode = null;
        if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var adminUserId))
        {
            adminCustomDoorCode = await this.Db.GuestDoorCodes
                .Where(g => g.UserId == adminUserId && g.PropertyId == propertyId)
                .Select(g => (string?)g.DoorCode)
                .FirstOrDefaultAsync(ct);
        }

        var adminEffectiveDoorCode = string.IsNullOrEmpty(adminCustomDoorCode) ? adminSecrets.DoorCode : adminCustomDoorCode;
        return new PhysicalAccessInfo
        {
            WifiNetwork = adminSecrets.WifiNetwork,
            WifiPassword = adminSecrets.WifiPassword,
            EffectiveDoorCode = adminEffectiveDoorCode,
        };
    }

    /// <summary>
    /// The check-in text for a guest, once their stay is close enough.
    /// </summary>
    /// <param name="principal"></param>
    /// <param name="propertyId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<string?> GetCheckinProceduresAsync(
        ClaimsPrincipal principal, Guid propertyId, CancellationToken ct = default)
    {
        var isAdmin = principal.HasClaim("is_admin", "true");

        if (!isAdmin)
        {
            if (!Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                return null;
            }

            var hasReservation = await this.Db.Reservations
                .AnyAsync(r => r.PropertyId == propertyId && r.UserId == userId && r.DeletedAt == null, ct);

            if (!hasReservation)
            {
                return null;
            }
        }

        return await this.Db.Properties
            .Where(p => p.Id == propertyId)
            .Select(p => (string?)p.CheckinProcedures)
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The global access window, for properties that do not set their own.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    private async Task<int> GetGlobalAccessWindowDaysAsync(CancellationToken ct)
    {
        var raw = await this.Db.Settings
            .Where(s => s.Key == "access_window_days")
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var days) ? days : 7;
    }

    private async Task<PropertySecrets?> LoadSecretsAsync(Guid propertyId, CancellationToken ct) =>
        await this.Db.Properties
            .Where(p => p.Id == propertyId)
            .Select(p => new PropertySecrets
            {
                WifiNetwork = p.WifiNetwork,
                WifiPassword = p.WifiPassword,
                DoorCode = p.DoorCode,
            })
            .FirstOrDefaultAsync(ct);

    private record PropertySecrets
    {
        public string WifiNetwork { get; init; } = string.Empty;

        public string WifiPassword { get; init; } = string.Empty;

        public string DoorCode { get; init; } = string.Empty;
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;

    /// <summary>
    /// The service used to read the configured check-in and check-out times.
    /// </summary>
    private readonly StayTimesService StayTimes;
}
