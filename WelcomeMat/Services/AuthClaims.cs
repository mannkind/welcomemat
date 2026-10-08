using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing the claims carried on the auth cookie.
/// </summary>
public static class AuthClaims
{
    /// <summary>
    /// The admin who started an impersonation session.
    /// </summary>
    public const string ImpersonatorIdClaim = "impersonator_id";

    /// <summary>
    /// The display name of the impersonating admin.
    /// </summary>
    public const string ImpersonatorNameClaim = "impersonator_name";

    /// <summary>
    /// Bumped on the user to invalidate every cookie already issued to them.
    /// </summary>
    public const string SecurityVersionClaim = "security_version";

    /// <summary>
    /// Build the principal for a signed-in user.
    /// </summary>
    /// <param name="user"></param>
    /// <param name="impersonatorId"></param>
    /// <param name="impersonatorName"></param>
    /// <returns></returns>
    public static ClaimsPrincipal BuildPrincipal(
        User user, Guid? impersonatorId = null, string? impersonatorName = null)
    {
        var identity = new ClaimsIdentity(CookieAuthenticationDefaults.AuthenticationScheme);
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
        identity.AddClaim(new Claim(ClaimTypes.Email, EmailNormalizer.Normalize(user.Email)));
        identity.AddClaim(new Claim(ClaimTypes.Name, user.Name));
        identity.AddClaim(new Claim(SecurityVersionClaim, user.SecurityVersion.ToString()));
        if (user.IsAdmin)
        {
            identity.AddClaim(new Claim("is_admin", "true"));
        }

        if (impersonatorId is { } id)
        {
            identity.AddClaim(new Claim(ImpersonatorIdClaim, id.ToString()));
            identity.AddClaim(new Claim(ImpersonatorNameClaim, impersonatorName ?? ""));
        }

        return new ClaimsPrincipal(identity);
    }
}
