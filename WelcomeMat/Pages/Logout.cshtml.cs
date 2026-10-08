using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the page that signs a user out.
/// </summary>
[AllowAnonymous]
public class LogoutModel : PageModel
{
    /// <summary>
    /// Sign out and send the user back to the login page.
    /// </summary>
    /// <returns></returns>
    public async Task<IActionResult> OnPostAsync()
    {
        await this.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return this.RedirectToPage("/Login");
    }
}
