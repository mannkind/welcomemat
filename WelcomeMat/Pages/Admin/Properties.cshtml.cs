using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Services;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
/// <summary>
/// An class representing the page listing the properties.
/// </summary>
public class PropertiesModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the PropertiesModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="geocode"></param>
    /// <param name="images"></param>
    public PropertiesModel(AppDbContext db, GeocodeService geocode, PropertyImageService images)
    {
        this.Db = db;
        this.Geocode = geocode;
        this.Images = images;
    }

    public List<PropertyEntity> Properties { get; private set; } = [];

    /// <summary>
    /// Show the properties.
    /// </summary>
    public void OnGet()
    {
        this.Properties = this.Db.Properties.OrderBy(p => p.Name).ToList();
        this.ViewData["Title"] = "Properties";
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
    }

    /// <summary>
    /// Add a property, geocoding its address.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="address"></param>
    /// <param name="wifiNetwork"></param>
    /// <param name="wifiPassword"></param>
    /// <param name="doorCode"></param>
    /// <param name="isActive"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostAdd(
        [FromForm] string? name,
        [FromForm] string? address,
        [FromForm] string? wifiNetwork,
        [FromForm] string? wifiPassword,
        [FromForm] string? doorCode,
        [FromForm] bool isActive,
        CancellationToken ct)
    {
        if (!InputLimits.IsValidShortLabel(name) || !InputLimits.IsValidShortLabel(address) ||
            !InputLimits.IsValidShortLabel(wifiNetwork) || !InputLimits.IsValidShortLabel(wifiPassword) ||
            !InputLimits.IsValidShortLabel(doorCode))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "One or more fields is too long.", this.BadRequest());
        }

        var coords = await this.Geocode.GeocodeAsync(address ?? "", ct);
        var prop = new PropertyEntity
        {
            Name = name ?? "",
            Address = address ?? "",
            WifiNetwork = wifiNetwork ?? "",
            WifiPassword = wifiPassword ?? "",
            DoorCode = doorCode ?? "",
            IcalToken = IcalService.NewToken(),
            IsActive = isActive,
            Latitude = coords?.Lat,
            Longitude = coords?.Lon,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        this.Db.Properties.Add(prop);
        await this.Db.SaveChangesAsync(ct);
        var editUrl = $"/admin/properties/{prop.Id}/edit";
        return AjaxResults.RedirectOrFetch(this.HttpContext, editUrl, this.Redirect(editUrl));
    }


    /// <summary>
    /// Mint a new feed token, retiring any URL already handed out.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostRotateIcal(Guid id)
    {
        var prop = this.Db.Properties.Find(id);
        if (prop is null)
        {
            return this.NotFound();
        }

        prop.IcalToken = IcalService.NewToken();
        prop.UpdatedAt = DateTime.UtcNow;
        this.Db.SaveChanges();
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }
    /// <summary>
    /// Delete a property and its photos.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostDelete([FromQuery] Guid id)
    {
        var prop = this.Db.Properties
            .Include(p => p.Reservations).ThenInclude(r => r.GuestbookEntries)
            .Include(p => p.BlackoutDates)
            .Include(p => p.FavoriteSpots)
            .Include(p => p.GuestDoorCodes)
            .Include(p => p.WalkingPaths)
            .Include(p => p.GuestbookEntries)
            .FirstOrDefault(p => p.Id == id);
        if (prop is not null)
        {
            this.Db.Properties.Remove(prop);
            this.Db.SaveChanges();
            this.Images.DeletePropertyDirectory(id);
        }
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to look up coordinates for an address.
    /// </summary>
    private readonly GeocodeService Geocode;

    /// <summary>
    /// The service used to store and serve property photos.
    /// </summary>
    private readonly PropertyImageService Images;
}
