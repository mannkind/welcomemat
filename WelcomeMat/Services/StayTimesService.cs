using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;

namespace WelcomeMat.Services;

/// <summary>
/// The check-in and check-out times for a stay.
/// </summary>
public record StayTimes
{
    public TimeOnly CheckIn { get; init; }

    public TimeOnly CheckOut { get; init; }
}

/// <summary>
/// An class representing a managed way to read the configured stay times.
/// </summary>
public class StayTimesService
{
    /// <summary>
    /// The settings key holding the global check-in time.
    /// </summary>
    public const string CheckInSettingKey = "check_in_time";

    /// <summary>
    /// The settings key holding the global check-out time.
    /// </summary>
    public const string CheckOutSettingKey = "check_out_time";

    /// <summary>
    /// The stay times used when nothing is configured.
    /// </summary>
    public static readonly StayTimes Defaults =
        new() { CheckIn = new TimeOnly(15, 0), CheckOut = new TimeOnly(11, 0) };

    /// <summary>
    /// How the times are stored; escaped, because ':' is culture-dependent.
    /// </summary>
    public const string TimeFormat = "HH\\:mm";

    /// <summary>
    /// Initializes a new instance of the StayTimesService class.
    /// </summary>
    /// <param name="db"></param>
    public StayTimesService(AppDbContext db)
    {
        this.Db = db;
    }

    /// <summary>
    /// Fetch the global stay times, falling back per-field to the defaults.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<StayTimes> GetGlobalAsync(CancellationToken ct = default)
    {
        var rows = await this.Db.Settings
            .Where(s => s.Key == CheckInSettingKey || s.Key == CheckOutSettingKey)
            .ToDictionaryAsync(s => s.Key, s => s.Value, ct);

        return new StayTimes
        {
            CheckIn = Parse(rows.GetValueOrDefault(CheckInSettingKey)) ?? Defaults.CheckIn,
            CheckOut = Parse(rows.GetValueOrDefault(CheckOutSettingKey)) ?? Defaults.CheckOut,
        };
    }

    /// <summary>
    /// Layer a property's overrides over the global times.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="global"></param>
    /// <returns></returns>
    public static StayTimes Resolve(TimeOnly? checkIn, TimeOnly? checkOut, StayTimes global) =>
        new() { CheckIn = checkIn ?? global.CheckIn, CheckOut = checkOut ?? global.CheckOut };

    /// <summary>
    /// Determine if checkout leaves time to clean before the next check-in.
    /// </summary>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <returns></returns>
    public static bool IsValidTurnover(TimeOnly checkIn, TimeOnly checkOut) =>
        checkOut <= checkIn;

    /// <summary>
    /// Parse a stored time; null when it is missing or malformed.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static TimeOnly? Parse(string? value) =>
        TimeOnly.TryParseExact(value, TimeFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// Format a time for storage.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string Format(TimeOnly value) =>
        value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
