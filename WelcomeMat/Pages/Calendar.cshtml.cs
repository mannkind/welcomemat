using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages;

/// <summary>
/// One cell of the calendar grid.
/// </summary>
public record DayInfo
{
    public DateOnly Date { get; init; }

    public bool InMonth { get; init; }

    public bool IsToday { get; init; }

    public bool IsPast { get; init; }

    public bool IsBlackout { get; init; }

    public string BlackoutReason { get; init; } = string.Empty;

    public Reservation? Reservation { get; init; }

    public bool ShowCancel { get; init; }

    public Reservation? CheckoutReservation { get; init; }
}

/// <summary>
/// An class representing the calendar page.
/// </summary>
public class CalendarModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the CalendarModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="reservationService"></param>
    /// <param name="clock"></param>
    public CalendarModel(AppDbContext db, ReservationService reservationService, PropertyClock clock)
    {
        this.Db = db;
        this.ReservationService = reservationService;
        this.Clock = clock;
    }

    public int Year { get; private set; }
    public int Month { get; private set; }
    public List<DayInfo> Days { get; private set; } = [];
    public PropertyEntity? SelectedProperty { get; private set; }
    public List<PropertyEntity> Properties { get; private set; } = [];
    public StaySettings Settings { get; private set; } =
        new() { MaxConsecutiveNights = 7, MinAdvanceNoticeDays = 1, MaxAdvanceBookingDays = 90 };
    public bool IsAdmin { get; private set; }
    public Guid UserId { get; private set; }
    public List<User> AllUsers { get; private set; } = [];

    /// <summary>
    /// Show the month.
    /// </summary>
    /// <param name="property"></param>
    /// <param name="year"></param>
    /// <param name="month"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnGetAsync(
        [FromRoute] Guid? property, [FromQuery] int? year, [FromQuery] int? month,
        CancellationToken ct = default)
    {
        this.IsAdmin = this.User.HasClaim("is_admin", "true");
        this.UserId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());

        if (this.IsAdmin)
        {
            this.AllUsers = await this.Db.Users.OrderBy(u => u.Name).ToListAsync(ct);
        }

        this.Properties = await this.Db.Properties.Where(p => p.IsActive).OrderBy(p => p.Name).ToListAsync(ct);

        if (property.HasValue)
        {
            this.SelectedProperty = this.Properties.FirstOrDefault(p => p.Id == property.Value);
        }

        this.SelectedProperty ??= this.Properties.FirstOrDefault();

        var today = this.SelectedProperty is not null
            ? this.Clock.Today(this.SelectedProperty.TimeZone)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        this.Year = year ?? today.Year;
        this.Month = month ?? today.Month;

        if (this.Year < 2000 || this.Year > 2100 || this.Month < 1 || this.Month > 12)
        {
            return this.BadRequest();
        }

        if (this.SelectedProperty is null)
        {
            return this.Page();
        }

        this.Settings = await this.ReservationService.GetSettingsAsync(this.SelectedProperty, ct);
        this.ViewData["CurrentPropertyId"] = this.SelectedProperty.Id;
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";

        var firstOfMonth = new DateOnly(this.Year, this.Month, 1);
        var calStart = firstOfMonth.AddDays(-(int)firstOfMonth.DayOfWeek);
        var calEnd = calStart.AddDays(42);

        var reservations = await this.Db.Reservations
            .Include(r => r.User)
            .Where(r => r.PropertyId == this.SelectedProperty.Id
                && r.DeletedAt == null
                && r.CheckIn < calEnd
                // Inclusive: a checkout on the grid's first day still marks that day
                && r.CheckOut >= calStart)
            .ToListAsync(ct);

        var blackouts = await this.Db.BlackoutDates
            .Where(b => b.PropertyId == this.SelectedProperty.Id || b.PropertyId == null)
            .ToListAsync(ct);

        this.Days = [];

        for (var d = calStart; d < calEnd; d = d.AddDays(1))
        {
            var res = reservations.FirstOrDefault(r => r.CheckIn <= d && r.CheckOut > d);
            var checkoutRes = reservations.FirstOrDefault(r => r.CheckOut == d);
            var blackout = blackouts.FirstOrDefault(b =>
                DateOnly.TryParseExact(b.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bd) && bd == d);

            this.Days.Add(new DayInfo
            {
                Date = d,
                InMonth = d.Month == this.Month,
                IsToday = d == today,
                IsPast = d < today,
                IsBlackout = blackout is not null,
                BlackoutReason = blackout?.Reason ?? "",
                Reservation = res,
                ShowCancel = res is not null && (this.IsAdmin || (res.UserId == this.UserId && res.CheckOut > today)),
                CheckoutReservation = checkoutRes,
            });
        }

        return this.Page();
    }

    /// <summary>
    /// The month before the one shown.
    /// </summary>
    /// <returns></returns>
    public DateOnly PrevMonth()
    {
        var d = new DateOnly(this.Year, this.Month, 1).AddMonths(-1);
        return d;
    }

    /// <summary>
    /// The month after the one shown.
    /// </summary>
    /// <returns></returns>
    public DateOnly NextMonth()
    {
        var d = new DateOnly(this.Year, this.Month, 1).AddMonths(1);
        return d;
    }

    /// <summary>
    /// The url for another month, keeping the selected property.
    /// </summary>
    /// <param name="d"></param>
    /// <param name="propertyId"></param>
    /// <returns></returns>
    public string CalendarUrl(DateOnly d, Guid? propertyId = null) =>
        (propertyId.HasValue ? $"/calendar/{propertyId}" : "/calendar") + $"?year={d.Year}&month={d.Month}";

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to validate the booking rules.
    /// </summary>
    private readonly ReservationService ReservationService;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;
}
