using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WelcomeMat.Data;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the landing page.
/// </summary>
public class IndexModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the IndexModel class.
    /// </summary>
    /// <param name="db"></param>
    public IndexModel(AppDbContext db)
    {
        this.Db = db;
    }

    /// <summary>
    /// Send the visitor where they belong.
    /// </summary>
    /// <returns></returns>
    public IActionResult OnGet()
    {
        var active = this.Db.Properties.Where(p => p.IsActive).ToList();
        if (active.Count == 1)
        {
            return this.Redirect($"/calendar/{active[0].Id}");
        }

        return this.RedirectToPage("/PropertyList");
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
