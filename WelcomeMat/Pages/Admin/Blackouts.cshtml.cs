using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
/// <summary>
/// An class representing the page where an admin blacks out dates.
/// </summary>
public class BlackoutsModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the BlackoutsModel class.
    /// </summary>
    /// <param name="db"></param>
    public BlackoutsModel(AppDbContext db)
    {
        this.Db = db;
    }

    public List<BlackoutDate> Blackouts { get; private set; } = [];
    public List<PropertyEntity> Properties { get; private set; } = [];

    /// <summary>
    /// Show the blackout dates.
    /// </summary>
    public void OnGet()
    {
        this.Blackouts = this.Db.BlackoutDates
            .Include(b => b.Property)
            .OrderBy(b => b.Date)
            .ToList();
        this.Properties = this.Db.Properties.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
        this.ViewData["Title"] = "Blackout Dates";
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
    }

    /// <summary>
    /// Black out a span of dates.
    /// </summary>
    /// <param name="startDate"></param>
    /// <param name="endDate"></param>
    /// <param name="reason"></param>
    /// <param name="propertyId"></param>
    /// <returns></returns>
    public IActionResult OnPostAdd(
        [FromForm] string startDate,
        [FromForm] string endDate,
        [FromForm] string? reason,
        [FromForm] Guid? propertyId)
    {
        var adminId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());

        if (!DateOnly.TryParseExact(startDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
            !DateOnly.TryParseExact(endDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "Please provide valid start and end dates.", this.BadRequest());
        }

        if (end < start)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "The end date must be on or after the start date.", this.BadRequest());
        }

        if (!InputLimits.IsValidNote(reason))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "Reason is too long.", this.BadRequest());
        }

        var spanDays = end.DayNumber - start.DayNumber + 1;
        if (spanDays > InputLimits.MaxBlackoutSpanDays)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext,
                $"A blackout range may span at most {InputLimits.MaxBlackoutSpanDays} days.", this.BadRequest());
        }

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var dateStr = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (!this.Db.BlackoutDates.Any(b => b.Date == dateStr && b.PropertyId == propertyId))
            {
                this.Db.BlackoutDates.Add(new BlackoutDate
                {
                    Date = dateStr,
                    Reason = reason ?? "",
                    PropertyId = propertyId,
                    CreatedBy = adminId,
                    CreatedAt = DateTime.UtcNow,
                });
            }
        }
        this.Db.SaveChanges();
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Lift a blackout.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostRemove([FromQuery] Guid id)
    {
        var entry = this.Db.BlackoutDates.Find(id);
        if (entry is not null)
        {
            this.Db.BlackoutDates.Remove(entry);
            this.Db.SaveChanges();
        }
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
