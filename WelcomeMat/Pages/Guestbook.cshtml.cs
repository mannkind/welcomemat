using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the guestbook page.
/// </summary>
public class GuestbookModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the GuestbookModel class.
    /// </summary>
    /// <param name="db"></param>
    public GuestbookModel(AppDbContext db)
    {
        this.Db = db;
    }

    public PropertyEntity? Property { get; private set; }
    public List<GuestbookEntry> Entries { get; private set; } = [];

    /// <summary>
    /// Show a property's guestbook.
    /// </summary>
    /// <param name="propertyId"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnGetAsync(Guid propertyId, CancellationToken ct = default)
    {
        this.Property = await this.Db.Properties.FindAsync([propertyId], ct);
        if (this.Property is null)
        {
            return this.NotFound();
        }

        this.Entries = await this.Db.GuestbookEntries
            .Include(e => e.User)
            .Include(e => e.Reservation)
            .Where(e => e.PropertyId == propertyId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
        this.ViewData["Title"] = $"Guestbook – {this.Property.Name}";
        return this.Page();
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
