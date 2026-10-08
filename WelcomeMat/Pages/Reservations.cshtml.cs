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

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the reservations timeline.
/// </summary>
public class ReservationsModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the ReservationsModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="coordinator"></param>
    /// <param name="clock"></param>
    /// <param name="publicUrls"></param>
    public ReservationsModel(AppDbContext db, ReservationCoordinator coordinator, PropertyClock clock, PublicUrlService publicUrls)
    {
        this.Db = db;
        this.Coordinator = coordinator;
        this.Clock = clock;
        this.PublicUrls = publicUrls;
    }

    public List<Reservation> MyReservations { get; private set; } = [];
    public Dictionary<Guid, GuestbookEntry> GuestbookEntries { get; private set; } = [];
    public string IcalUrl { get; private set; } = "";
    public bool IsAdmin { get; private set; }

    /// <summary>
    /// Today, in the property's own time zone.
    /// </summary>
    /// <param name="property"></param>
    /// <returns></returns>
    public DateOnly TodayFor(Property property) => this.Clock.Today(property.TimeZone);

    /// <summary>
    /// Show the upcoming and past stays.
    /// </summary>
    /// <param name="ct"></param>
    public async Task OnGetAsync(CancellationToken ct = default)
    {
        var userId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        this.IsAdmin = this.User.HasClaim("is_admin", "true");

        this.MyReservations = await this.Db.Reservations
            .Include(r => r.Property)
            .Where(r => r.UserId == userId && r.DeletedAt == null)
            .OrderBy(r => r.CheckIn)
            .ToListAsync(ct);

        var reservationIds = this.MyReservations.Select(r => r.Id).ToList();
        this.GuestbookEntries = await this.Db.GuestbookEntries
            .Where(e => reservationIds.Contains(e.ReservationId))
            .ToDictionaryAsync(e => e.ReservationId, ct);

        var user = await this.Db.Users.FindAsync([userId], ct);
        if (!string.IsNullOrEmpty(user?.CalendarToken))
        {
            this.IcalUrl = this.PublicUrls.BuildWebcal(
                $"/ical/{user.CalendarToken}",
                this.Request.Host);
        }

        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
        this.ViewData["CurrentPage"] = "reservations";
        this.ViewData["Title"] = "My Reservations";
    }

    /// <summary>
    /// Mint a new personal feed token, retiring any URL already handed out.
    /// </summary>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostRotateIcalAsync(CancellationToken ct = default)
    {
        var userId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        var user = await this.Db.Users.FindAsync([userId], ct);
        if (user is null)
        {
            return this.NotFound();
        }

        user.CalendarToken = IcalService.NewToken();
        user.UpdatedAt = DateTime.UtcNow;
        await this.Db.SaveChangesAsync(ct);
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Sign the guestbook for a stay.
    /// </summary>
    /// <param name="reservationId"></param>
    /// <param name="message"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostGuestbookAsync(
        [FromForm(Name = "reservation_id")] Guid reservationId,
        [FromForm] string? message,
        CancellationToken ct = default)
    {
        var userId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());

        var reservation = await this.Db.Reservations
            .Include(r => r.Property)
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct);
        if (reservation is null || reservation.UserId != userId || reservation.DeletedAt != null)
        {
            return this.BadRequest("Reservation not found.");
        }

        var today = this.Clock.Today(reservation.Property!.TimeZone);
        if (!GuestbookService.IsInWriteWindow(reservation.CheckOut, today))
        {
            return this.BadRequest("Guestbook entries can only be written starting the last day of your stay through 14 days after checkout.");
        }

        if (!InputLimits.IsValidNote(message))
        {
            return this.BadRequest("Message is too long.");
        }

        var existing = await this.Db.GuestbookEntries.FirstOrDefaultAsync(e => e.ReservationId == reservationId, ct);
        if (existing is null)
        {
            this.Db.GuestbookEntries.Add(new GuestbookEntry
            {
                Id = Guid.NewGuid(),
                ReservationId = reservationId,
                PropertyId = reservation.PropertyId,
                UserId = userId,
                Message = message ?? "",
                CreatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            existing.Message = message ?? "";
            existing.UpdatedAt = DateTime.UtcNow;
        }
        await this.Db.SaveChangesAsync(ct);

        return this.RedirectToPage();
    }

    /// <summary>
    /// Cancel a stay.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="checkIn"></param>
    /// <param name="checkOut"></param>
    /// <param name="notes"></param>
    /// <param name="targetUserId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostAsync(
        [FromForm(Name = "property_id")] Guid propertyId,
        [FromForm(Name = "check_in")] string? checkIn,
        [FromForm(Name = "check_out")] string? checkOut,
        [FromForm] string? notes,
        [FromForm(Name = "user_id")] Guid? targetUserId,
        CancellationToken ct = default)
    {
        if (!DateOnly.TryParseExact(checkIn, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var ciDate) ||
            !DateOnly.TryParseExact(checkOut, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var coDate))
        {
            return this.BadRequest("Invalid dates.");
        }

        if (!InputLimits.IsValidNote(notes))
        {
            return this.BadRequest("Notes are too long.");
        }

        var request = new ReservationCoordinator.CreateRequest
        {
            PropertyId = propertyId,
            CheckIn = ciDate,
            CheckOut = coDate,
            Notes = notes,
            TargetUserId = targetUserId,
        };
        var result = await this.Coordinator.CreateAsync(request, this.User, bypassValidation: false, ct);

        if (result.Status != ReservationMutationStatus.Success)
        {
            var error = result.Error ?? "Validation failed.";
            if (this.Request.Headers["X-Requested-With"] == "fetch")
            {
                return this.BadRequest(error);
            }

            this.TempData["Flash"] = error;
            return this.Redirect($"/calendar/{propertyId}");
        }

        var redirectUrl = $"/calendar/{propertyId}?year={ciDate.Year}&month={ciDate.Month}";
        if (this.Request.Headers["X-Requested-With"] == "fetch")
        {
            this.Response.Headers["X-Redirect"] = redirectUrl;
            return new OkResult();
        }
        return this.Redirect(redirectUrl);
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to apply reservation changes.
    /// </summary>
    private readonly ReservationCoordinator Coordinator;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;

    /// <summary>
    /// The service used to build externally reachable URLs.
    /// </summary>
    private readonly PublicUrlService PublicUrls;
}
