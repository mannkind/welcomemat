using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
/// <summary>
/// An class representing the page where a property is edited.
/// </summary>
public class PropertyEditModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the PropertyEditModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="geocode"></param>
    /// <param name="options"></param>
    /// <param name="publicUrls"></param>
    /// <param name="images"></param>
    /// <param name="stayTimes"></param>
    public PropertyEditModel(AppDbContext db, GeocodeService geocode, IOptions<AppOptions> options, PublicUrlService publicUrls, PropertyImageService images, StayTimesService stayTimes)
    {
        this.Db = db;
        this.Geocode = geocode;
        this.Options = options;
        this.PublicUrls = publicUrls;
        this.Images = images;
        this.StayTimes = stayTimes;
    }

    public PropertyEntity? Property { get; private set; }
    public List<User> Users { get; private set; } = [];
    public List<GuestDoorCode> GuestDoorCodes { get; private set; } = [];
    public List<string> Photos { get; private set; } = [];
    public bool HasThumbnail { get; private set; }
    public long ThumbVersion { get; private set; }
    public int GlobalMaxConsecutiveNights { get; private set; }
    public int GlobalMinAdvanceNoticeDays { get; private set; }
    public int GlobalMaxAdvanceBookingDays { get; private set; }
    public int GlobalAccessWindowDays { get; private set; }
    public StayTimes GlobalStayTimes { get; private set; } = StayTimesService.Defaults;

    /// <summary>
    /// Show the property being edited.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnGet(Guid id)
    {
        this.Property = this.Db.Properties.Find(id);
        if (this.Property is null)
        {
            return this.NotFound();
        }

        var globalSettings = this.Db.Settings.ToDictionary(s => s.Key, s => s.Value);
        this.GlobalMaxConsecutiveNights = GetInt(globalSettings, "max_consecutive_nights", 7);
        this.GlobalMinAdvanceNoticeDays = GetInt(globalSettings, "min_advance_notice_days", 1);
        this.GlobalMaxAdvanceBookingDays = GetInt(globalSettings, "max_advance_booking_days", 90);
        this.GlobalAccessWindowDays = GetInt(globalSettings, "access_window_days", 7);
        this.GlobalStayTimes = new StayTimes
        {
            CheckIn = StayTimesService.Parse(globalSettings.GetValueOrDefault(StayTimesService.CheckInSettingKey))
                ?? StayTimesService.Defaults.CheckIn,
            CheckOut = StayTimesService.Parse(globalSettings.GetValueOrDefault(StayTimesService.CheckOutSettingKey))
                ?? StayTimesService.Defaults.CheckOut,
        };






        this.Users = this.Db.Users.OrderBy(u => u.Name).ToList();
        this.GuestDoorCodes = this.Db.GuestDoorCodes.Where(g => g.PropertyId == id).ToList();
        var imageDir = Path.Combine(this.Options.Value.ImagesPath, id.ToString());
        this.Photos = this.Images.ListGalleryFilenames(id).Select(f => $"/images/{id}/{f}").ToList();

        this.HasThumbnail = ThumbnailService.HasThumbnail(imageDir);
        if (this.HasThumbnail)
        {
            this.ThumbVersion = System.IO.File.GetLastWriteTimeUtc(
                Path.Combine(imageDir, ThumbnailService.ThumbFileName)).Ticks;
        }

        this.ViewData["Title"] = $"Edit {this.Property.Name}";
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
        return this.Page();
    }

    private static int GetInt(Dictionary<string, string> d, string key, int fallback) =>
        d.TryGetValue(key, out var v) && int.TryParse(v, out var i) ? i : fallback;

    /// <summary>
    /// Add photos to the gallery.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="photos"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostUploadPhotos(Guid id, IList<IFormFile> photos, CancellationToken ct)
    {
        var result = await this.Images.SaveJpegsAsync(id, [.. photos], ct);
        this.TempData["Flash"] = result.Success
            ? (result.SavedCount == 1 ? "1 photo uploaded." : $"{result.SavedCount} photos uploaded.")
            : result.Error;
        return this.RedirectToPage(null, null, new { id }, "photos");
    }

    /// <summary>
    /// Remove one photo.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IActionResult OnPostDeletePhoto(Guid id, [FromForm] string filename)
    {
        if (filename.Contains('/') || filename.Contains('\\'))
        {
            return this.BadRequest();
        }

        var path = Path.Combine(this.Options.Value.ImagesPath, id.ToString(), filename);
        if (System.IO.File.Exists(path))
        {
            System.IO.File.Delete(path);
        }

        this.TempData["Flash"] = "Photo deleted.";
        return this.RedirectToPage(null, null, new { id }, "photos");
    }

    /// <summary>
    /// Save the gallery order.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="filenames"></param>
    /// <returns></returns>
    public IActionResult OnPostReorderPhotos(Guid id, [FromForm] List<string> filenames)
    {
        if (filenames.Any(f => f.Contains('/') || f.Contains('\\')))
        {
            return this.BadRequest();
        }

        var imageDir = Path.Combine(this.Options.Value.ImagesPath, id.ToString());
        if (!Directory.Exists(imageDir))
        {
            return this.RedirectToPage(null, null, new { id }, "photos");
        }

        static string ExtractUuid(string filename)
        {
            var name = Path.GetFileNameWithoutExtension(filename);
            var sep = name.IndexOf('_');
            return sep > 0 && name[..sep].All(char.IsAsciiDigit) ? name[(sep + 1)..] : name;
        }

        foreach (var filename in filenames)
        {
            var src = Path.Combine(imageDir, filename);
            if (!System.IO.File.Exists(src))
            {
                continue;
            }

            System.IO.File.Move(src, Path.Combine(imageDir, $"tmp_{ExtractUuid(filename)}.jpg"), overwrite: true);
        }
        for (var i = 0; i < filenames.Count; i++)
        {
            var uuid = ExtractUuid(filenames[i]);
            var tmp = Path.Combine(imageDir, $"tmp_{uuid}.jpg");
            if (!System.IO.File.Exists(tmp))
            {
                continue;
            }

            System.IO.File.Move(tmp, Path.Combine(imageDir, $"{i + 1:D3}_{uuid}.jpg"), overwrite: true);
        }

        this.TempData["Flash"] = "Photo order saved.";
        return this.RedirectToPage(null, null, new { id }, "photos");
    }

    /// <summary>
    /// Use a gallery photo as the thumbnail.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="filename"></param>
    /// <returns></returns>
    public IActionResult OnPostSetThumbnail(Guid id, [FromForm] string filename)
    {
        if (filename.Contains('/') || filename.Contains('\\'))
        {
            return this.BadRequest();
        }

        var imageDir = Path.Combine(this.Options.Value.ImagesPath, id.ToString());
        this.TempData["Flash"] = ThumbnailService.SetThumbnail(imageDir, filename)
            ? "Thumbnail set."
            : "Could not set thumbnail.";
        return this.RedirectToPage(null, null, new { id }, "photos");
    }

    /// <summary>
    /// Drop the thumbnail.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostRemoveThumbnail(Guid id)
    {
        var imageDir = Path.Combine(this.Options.Value.ImagesPath, id.ToString());
        ThumbnailService.RemoveThumbnail(imageDir);
        this.TempData["Flash"] = "Thumbnail removed.";
        return this.RedirectToPage(null, null, new { id }, "photos");
    }

    /// <summary>
    /// Save the property's details and booking rules.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="name"></param>
    /// <param name="address"></param>
    /// <param name="wifiNetwork"></param>
    /// <param name="wifiPassword"></param>
    /// <param name="doorCode"></param>
    /// <param name="timeZone"></param>
    /// <param name="information"></param>
    /// <param name="checkinProcedures"></param>
    /// <param name="checkoutProcedures"></param>
    /// <param name="isActive"></param>
    /// <param name="maxConsecutiveNights"></param>
    /// <param name="minAdvanceNoticeDays"></param>
    /// <param name="maxAdvanceBookingDays"></param>
    /// <param name="accessWindowDays"></param>
    /// <param name="checkInTime"></param>
    /// <param name="checkOutTime"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostUpdate(
        Guid id,
        [FromForm] string? name,
        [FromForm] string? address,
        [FromForm] string? wifiNetwork,
        [FromForm] string? wifiPassword,
        [FromForm] string? doorCode,
        [FromForm] string? timeZone,
        [FromForm] string? information,
        [FromForm] string? checkinProcedures,
        [FromForm] string? checkoutProcedures,
        [FromForm] bool isActive,
        [FromForm] int? maxConsecutiveNights,
        [FromForm] int? minAdvanceNoticeDays,
        [FromForm] int? maxAdvanceBookingDays,
        [FromForm] int? accessWindowDays,
        [FromForm] string? checkInTime,
        [FromForm] string? checkOutTime,
        CancellationToken ct)
    {
        var prop = this.Db.Properties.Find(id);
        if (prop is null)
        {
            return this.NotFound();
        }

        if (!InputLimits.IsValidShortLabel(name) || !InputLimits.IsValidShortLabel(address) ||
            !InputLimits.IsValidShortLabel(wifiNetwork) || !InputLimits.IsValidShortLabel(wifiPassword) ||
            !InputLimits.IsValidShortLabel(doorCode))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "One or more fields is too long.", this.BadRequest());
        }

        if (!InputLimits.IsValidLongText(information) || !InputLimits.IsValidLongText(checkinProcedures) ||
            !InputLimits.IsValidLongText(checkoutProcedures))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "One or more fields is too long.", this.BadRequest());
        }

        if (!TimeZoneValidation.IsValidIanaId(timeZone))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "Time zone must be a valid IANA time zone id.", this.BadRequest());
        }

        if (maxConsecutiveNights is { } mcn && (mcn < InputLimits.MinMaxConsecutiveNights || mcn > InputLimits.MaxMaxConsecutiveNights))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Maximum consecutive nights must be between {InputLimits.MinMaxConsecutiveNights} and {InputLimits.MaxMaxConsecutiveNights}.",
                this.BadRequest());
        }

        if (minAdvanceNoticeDays is { } mand && (mand < InputLimits.MinMinAdvanceNoticeDays || mand > InputLimits.MaxMinAdvanceNoticeDays))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Minimum advance notice must be between {InputLimits.MinMinAdvanceNoticeDays} and {InputLimits.MaxMinAdvanceNoticeDays} days.",
                this.BadRequest());
        }

        if (maxAdvanceBookingDays is { } mabd && (mabd < InputLimits.MinMaxAdvanceBookingDays || mabd > InputLimits.MaxMaxAdvanceBookingDays))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Maximum advance booking must be between {InputLimits.MinMaxAdvanceBookingDays} and {InputLimits.MaxMaxAdvanceBookingDays} days.",
                this.BadRequest());
        }

        if (accessWindowDays is { } awd && (awd < InputLimits.MinAccessWindowDays || awd > InputLimits.MaxAccessWindowDays))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Access window must be between {InputLimits.MinAccessWindowDays} and {InputLimits.MaxAccessWindowDays} days.",
                this.BadRequest());
        }

        var overrideCheckIn = string.IsNullOrWhiteSpace(checkInTime) ? null : StayTimesService.Parse(checkInTime);
        var overrideCheckOut = string.IsNullOrWhiteSpace(checkOutTime) ? null : StayTimesService.Parse(checkOutTime);

        if ((!string.IsNullOrWhiteSpace(checkInTime) && overrideCheckIn is null) ||
            (!string.IsNullOrWhiteSpace(checkOutTime) && overrideCheckOut is null))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                "Check-in and check-out times must be times of day (HH:mm).", this.BadRequest());
        }

        var resolved = StayTimesService.Resolve(
            overrideCheckIn, overrideCheckOut, await this.StayTimes.GetGlobalAsync(ct));
        if (!StayTimesService.IsValidTurnover(resolved.CheckIn, resolved.CheckOut))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                "Check-out time must not be later than check-in time, or same-day turnovers would overlap.",
                this.BadRequest());
        }

        var addressChanged = prop.Address != (address ?? "");
        prop.Name = name ?? "";
        prop.Address = address ?? "";
        prop.WifiNetwork = wifiNetwork ?? "";
        prop.WifiPassword = wifiPassword ?? "";
        prop.DoorCode = doorCode ?? "";
        prop.TimeZone = timeZone!;
        prop.Information = information ?? "";
        prop.CheckinProcedures = checkinProcedures ?? "";
        prop.CheckoutProcedures = checkoutProcedures ?? "";
        prop.IsActive = isActive;
        prop.MaxConsecutiveNights = maxConsecutiveNights;
        prop.MinAdvanceNoticeDays = minAdvanceNoticeDays;
        prop.MaxAdvanceBookingDays = maxAdvanceBookingDays;
        prop.AccessWindowDays = accessWindowDays;
        prop.CheckInTime = overrideCheckIn;
        prop.CheckOutTime = overrideCheckOut;
        prop.UpdatedAt = DateTime.UtcNow;

        if (addressChanged)
        {
            var coords = await this.Geocode.GeocodeAsync(address ?? "", ct);
            if (coords.HasValue)
            {
                prop.Latitude = coords.Value.Lat;
                prop.Longitude = coords.Value.Lon;
            }
        }

        await this.Db.SaveChangesAsync(ct);
        this.TempData["Flash"] = "Property updated.";
        return this.RedirectToPage(null, null, new { id }, "details");
    }





    /// <summary>
    /// Save the per-guest door codes.
    /// </summary>
    /// <param name="id"></param>
    /// <param name="userId"></param>
    /// <param name="doorCode"></param>
    /// <returns></returns>
    public IActionResult OnPostGuestDoorCode(Guid id, [FromForm] List<Guid> userId, [FromForm] List<string?> doorCode)
    {
        for (var i = 0; i < userId.Count; i++)
        {
            var uid = userId[i];
            var code = i < doorCode.Count ? doorCode[i] ?? "" : "";
            var existing = this.Db.GuestDoorCodes.Find(uid, id);
            if (existing is null)
            {
                this.Db.GuestDoorCodes.Add(new GuestDoorCode
                {
                    UserId = uid,
                    PropertyId = id,
                    DoorCode = code,
                });
            }
            else
            {
                existing.DoorCode = code;
            }
        }
        this.Db.SaveChanges();
        return this.RedirectToPage(null, null, new { id }, "door-codes");
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
    /// The application options.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The service used to build externally reachable URLs.
    /// </summary>
    private readonly PublicUrlService PublicUrls;

    /// <summary>
    /// The service used to store and serve property photos.
    /// </summary>
    private readonly PropertyImageService Images;

    /// <summary>
    /// The service used to read the configured check-in and check-out times.
    /// </summary>
    private readonly StayTimesService StayTimes;
}
