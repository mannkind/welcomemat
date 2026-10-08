using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
/// <summary>
/// An class representing the page holding the application-wide settings.
/// </summary>
public class SettingsModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the SettingsModel class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="publicUrls"></param>
    public SettingsModel(AppDbContext db, PublicUrlService publicUrls)
    {
        this.Db = db;
        this.PublicUrls = publicUrls;
    }

    public Dictionary<string, string> Values { get; private set; } = [];
    public string AdminIcalUrl { get; private set; } = "";

    /// <summary>
    /// Show the settings.
    /// </summary>
    public void OnGet()
    {
        this.Values = this.Db.Settings.ToDictionary(s => s.Key, s => s.Value);
        this.ViewData["Title"] = "Settings";
        this.ViewData["SiteTitle"] = this.Values.GetValueOrDefault("site_title", "WelcomeMat");

        var adminToken = this.Values.GetValueOrDefault("admin_ical_token", "");
        if (!string.IsNullOrEmpty(adminToken))
        {
            this.AdminIcalUrl = this.PublicUrls.BuildWebcal($"/ical/{adminToken}", this.Request.Host);
        }
    }

    /// <summary>
    /// Save the settings.
    /// </summary>
    /// <param name="siteTitle"></param>
    /// <param name="maxConsecutiveNights"></param>
    /// <param name="minAdvanceNoticeDays"></param>
    /// <param name="maxAdvanceBookingDays"></param>
    /// <param name="accessWindowDays"></param>
    /// <param name="checkInTime"></param>
    /// <param name="checkOutTime"></param>
    /// <returns></returns>
    public IActionResult OnPost(
        [FromForm] string? siteTitle,
        [FromForm] int maxConsecutiveNights,
        [FromForm] int minAdvanceNoticeDays,
        [FromForm] int maxAdvanceBookingDays,
        [FromForm] int accessWindowDays,
        [FromForm] string? checkInTime,
        [FromForm] string? checkOutTime)
    {
        if (!InputLimits.IsValidShortLabel(siteTitle))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "Site title is too long.", this.BadRequest());
        }

        if (maxConsecutiveNights < InputLimits.MinMaxConsecutiveNights || maxConsecutiveNights > InputLimits.MaxMaxConsecutiveNights)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Maximum consecutive nights must be between {InputLimits.MinMaxConsecutiveNights} and {InputLimits.MaxMaxConsecutiveNights}.",
                this.BadRequest());
        }

        if (minAdvanceNoticeDays < InputLimits.MinMinAdvanceNoticeDays || minAdvanceNoticeDays > InputLimits.MaxMinAdvanceNoticeDays)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Minimum advance notice must be between {InputLimits.MinMinAdvanceNoticeDays} and {InputLimits.MaxMinAdvanceNoticeDays} days.",
                this.BadRequest());
        }

        if (maxAdvanceBookingDays < InputLimits.MinMaxAdvanceBookingDays || maxAdvanceBookingDays > InputLimits.MaxMaxAdvanceBookingDays)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Maximum advance booking must be between {InputLimits.MinMaxAdvanceBookingDays} and {InputLimits.MaxMaxAdvanceBookingDays} days.",
                this.BadRequest());
        }

        if (accessWindowDays < InputLimits.MinAccessWindowDays || accessWindowDays > InputLimits.MaxAccessWindowDays)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"Access window must be between {InputLimits.MinAccessWindowDays} and {InputLimits.MaxAccessWindowDays} days.",
                this.BadRequest());
        }

        var parsedCheckIn = StayTimesService.Parse(checkInTime);
        var parsedCheckOut = StayTimesService.Parse(checkOutTime);
        if (parsedCheckIn is null || parsedCheckOut is null)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                "Check-in and check-out times must be times of day (HH:mm).", this.BadRequest());
        }

        if (!StayTimesService.IsValidTurnover(parsedCheckIn.Value, parsedCheckOut.Value))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                "Check-out time must not be later than check-in time, or same-day turnovers would overlap.",
                this.BadRequest());
        }

        this.Upsert("site_title", siteTitle ?? "");
        this.Upsert("max_consecutive_nights", maxConsecutiveNights.ToString());
        this.Upsert("min_advance_notice_days", minAdvanceNoticeDays.ToString());
        this.Upsert("max_advance_booking_days", maxAdvanceBookingDays.ToString());
        this.Upsert("access_window_days", accessWindowDays.ToString());
        this.Upsert(StayTimesService.CheckInSettingKey, StayTimesService.Format(parsedCheckIn.Value));
        this.Upsert(StayTimesService.CheckOutSettingKey, StayTimesService.Format(parsedCheckOut.Value));
        this.Db.SaveChanges();
        if (this.Request.IsFetch())
        {
            return new OkResult();
        }

        this.TempData["Flash"] = "Settings saved.";
        return this.RedirectToPage();
    }

    /// <summary>
    /// Mint a new admin feed token, retiring any URL already handed out.
    /// </summary>
    /// <returns></returns>
    public IActionResult OnPostRotateAdminIcal()
    {
        this.Upsert("admin_ical_token", IcalService.NewToken());
        this.Db.SaveChanges();
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Write a setting, inserting it when it is not there yet.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="value"></param>
    private void Upsert(string key, string value)
    {
        var setting = this.Db.Settings.Find(key);
        if (setting is null)
        {
            this.Db.Settings.Add(new Data.Entities.Setting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The service used to build externally reachable URLs.
    /// </summary>
    private readonly PublicUrlService PublicUrls;
}
