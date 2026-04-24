using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WelcomeMat.Authentication;
using WelcomeMat.Data;
namespace WelcomeMat.Pages;

[AllowAnonymous]
/// <summary>
/// An class representing the login page.
/// </summary>
public class LoginModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the LoginModel class.
    /// </summary>
    /// <param name="db"></param>
    public LoginModel(AppDbContext db)
    {
        this.Db = db;
    }

    public string? Error { get; private set; }

    public string? RejectedEmail { get; private set; }

    /// <summary>
    /// Show the login page, naming the address that was turned away.
    /// </summary>
    /// <param name="error"></param>
    /// <param name="returnUrl"></param>
    /// <returns></returns>
    public IActionResult OnGet([FromQuery] string? error, [FromQuery] string? returnUrl)
    {
        if (this.User.Identity?.IsAuthenticated == true)
        {
            return this.RedirectToPage("/Index");
        }

        this.Error = error;
        if (error == "not_allowed")
        {
            this.RejectedEmail = RejectedLoginCookie.Read(this.Request);
        }

        RejectedLoginCookie.Clear(this.Response);
        this.ViewData["ReturnUrl"] = returnUrl;

        var title = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value;
        this.ViewData["SiteTitle"] = title ?? "WelcomeMat";

        return this.Page();
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
