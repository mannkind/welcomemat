using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Pages.Models;
using WelcomeMat.Services;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the page a guest reads about their stay.
/// </summary>
public class PropertyModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the PropertyModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="propertyAccess"></param>
    /// <param name="images"></param>
    /// <param name="clock"></param>
    /// <param name="logger"></param>
    public PropertyModel(AppDbContext db, PropertyAccessService propertyAccess, PropertyImageService images, PropertyClock clock, ILogger<PropertyModel> logger)
    {
        this.Db = db;
        this.PropertyAccess = propertyAccess;
        this.Images = images;
        this.Clock = clock;
        this.Logger = logger;
    }

    public PropertyPageView? Property { get; private set; }
    public List<FavoriteSpot> FavoriteSpots { get; private set; } = [];
    public List<WalkingPath> WalkingPaths { get; private set; } = [];
    public List<GuestbookEntry> GuestbookEntries { get; private set; } = [];
    public IReadOnlyList<string> GalleryImages { get; private set; } = [];
    public PhysicalAccessInfo? Access { get; private set; }
    public string? CheckinProcedures { get; private set; }
    public bool IsAdmin { get; private set; }
    public Reservation? NextReservation { get; private set; }

    /// <summary>
    /// Show a property, with only the details this guest may see.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnGetAsync([FromRoute] Guid? id, CancellationToken ct)
    {
        if (!id.HasValue)
        {
            var activeIds = await this.Db.Properties.Where(p => p.IsActive)
                .OrderBy(p => p.Name)
                .Select(p => p.Id)
                .ToListAsync(ct);
            return activeIds.Count == 1
                ? this.Redirect($"/property/{activeIds[0]}")
                : this.RedirectToPage("/PropertyList");
        }

        this.Property = await this.Db.Properties
            .Where(p => p.Id == id.Value && p.IsActive)
            .Select(p => new PropertyPageView
            {
                Id = p.Id,
                Name = p.Name,
                Address = p.Address,
                Information = p.Information,
                CheckoutProcedures = p.CheckoutProcedures,
                IsActive = p.IsActive,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                TimeZone = p.TimeZone,
            })
            .FirstOrDefaultAsync(ct);

        if (this.Property is null)
        {
            return this.NotFound();
        }

        this.FavoriteSpots = await this.Db.FavoriteSpots
            .Where(f => f.PropertyId == this.Property.Id)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        var allWalkingPaths = await this.Db.WalkingPaths
            .Where(w => w.PropertyId == this.Property.Id)
            .OrderBy(w => w.SortOrder)
            .ToListAsync(ct);

        this.WalkingPaths = [];
        foreach (var path in allWalkingPaths)
        {
            if (WalkingPathValidator.TryParse(path.Coordinates, out _, out var error))
            {
                this.WalkingPaths.Add(path);
            }
            else
            {
                this.Logger.LogWarning(
                    "Skipping walking path {WalkingPathId} with invalid coordinates: {Error}",
                    path.Id, error);
            }
        }

        this.GuestbookEntries = await this.Db.GuestbookEntries
            .Include(e => e.User)
            .Include(e => e.Reservation)
            .Where(e => e.PropertyId == this.Property.Id)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        this.GalleryImages = this.Images.ListGalleryFilenames(this.Property.Id);

        this.IsAdmin = this.User.HasClaim("is_admin", "true");
        this.Access = await this.PropertyAccess.GetAsync(this.User, this.Property.Id, ct);
        this.CheckinProcedures = await this.PropertyAccess.GetCheckinProceduresAsync(this.User, this.Property.Id, ct);

        var userId = Guid.TryParse(this.User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUserId)
            ? parsedUserId
            : Guid.Empty;
        var today = this.Clock.Today(this.Property.TimeZone);
        var candidates = await this.Db.Reservations
            .Where(r => r.PropertyId == this.Property.Id
                     && r.UserId == userId
                     && r.DeletedAt == null)
            .ToListAsync(ct);
        this.NextReservation = ReservationService.SelectCurrentOrNext(candidates, today);

        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
        this.ViewData["CurrentPage"] = "property";
        this.ViewData["CurrentPropertyId"] = this.Property.Id;
        this.ViewData["Title"] = this.Property.Name;
        return this.Page();
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to decide what a guest may see.
    /// </summary>
    private readonly PropertyAccessService PropertyAccess;

    /// <summary>
    /// The service used to store and serve property photos.
    /// </summary>
    private readonly PropertyImageService Images;

    /// <summary>
    /// The clock used to read the time in a property's own zone.
    /// </summary>
    private readonly PropertyClock Clock;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<PropertyModel> Logger;
}
