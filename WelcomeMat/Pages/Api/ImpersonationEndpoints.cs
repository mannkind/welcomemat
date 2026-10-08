using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Api;

/// <summary>
/// Extensions for mapping the endpoint that ends an impersonation session.
/// </summary>
public static class ImpersonationEndpointsExtensions
{
    /// <summary>
    /// Map /impersonate/stop. Re-checks the admin flag before handing the
    /// session back, in case the admin lost it mid-impersonation.
    /// </summary>
    /// <param name="app"></param>
    public static void MapImpersonationEndpoints(this WebApplication app)
    {
        app.MapPost("/impersonate/stop", async (AppDbContext db, HttpContext http, IAntiforgery antiforgery) =>
        {
            if (await ReservationEndpointsExtensions.RejectIfForgedAsync(antiforgery, http) is { } forged)
            {
                return forged;
            }

            var raw = http.User.FindFirstValue(AuthClaims.ImpersonatorIdClaim);
            if (!Guid.TryParse(raw, out var adminId))
            {
                return Results.Redirect("/");
            }

            var admin = await db.Users.FindAsync(adminId);
            if (admin is null || !admin.IsAdmin)
            {
                await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return Results.Redirect("/login");
            }

            await http.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                AuthClaims.BuildPrincipal(admin));

            return Results.Redirect("/");
        }).RequireAuthorization();
    }
}
