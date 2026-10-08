using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WelcomeMat.Authentication;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing the page that hands a user off to Google.
/// </summary>
[AllowAnonymous]
public class GoogleLoginModel : PageModel
{
    /// <summary>
    /// Challenge Google, keeping a local return url for afterwards.
    /// </summary>
    /// <param name="returnUrl"></param>
    /// <param name="switchAccount"></param>
    /// <returns></returns>
    public IActionResult OnGet([FromQuery] string? returnUrl, [FromQuery] bool switchAccount)
    {
        var redirect = !string.IsNullOrEmpty(returnUrl) && this.Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : "/calendar";
        var properties = new AuthenticationProperties { RedirectUri = redirect };
        if (switchAccount)
        {
            properties.Items[GoogleOidcEvents.SelectAccountItem] = "true";
        }

        return this.Challenge(properties, "Google");
    }
}
