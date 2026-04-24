using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WelcomeMat.Data;
using PropertyEntity = WelcomeMat.Data.Entities.Property;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the page listing the properties a guest may see.
/// </summary>
public class PropertyListModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the PropertyListModel class.
    /// </summary>
    /// <param name="db"></param>
    public PropertyListModel(AppDbContext db)
    {
        this.Db = db;
    }

    public List<PropertyEntity> Properties { get; private set; } = [];
    public bool IsAdmin { get; private set; }

    /// <summary>
    /// Show the properties this guest may see.
    /// </summary>
    public void OnGet()
    {
        this.Properties = this.Db.Properties.Where(p => p.IsActive).OrderBy(p => p.Name).ToList();
        this.IsAdmin = this.User.HasClaim("is_admin", "true");
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
        this.ViewData["CurrentPage"] = "properties";
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
