using System;
using Microsoft.AspNetCore.Http;

namespace WelcomeMat.Authentication;

/// <summary>
/// An class representing a way to carry a rejected email back to the login page,
/// so it can say which address was turned away.
/// </summary>
public static class RejectedLoginCookie
{
    /// <summary>
    /// The cookie name.
    /// </summary>
    public const string Name = "wm_rejected_email";

    /// <summary>
    /// Scoped to the login page; nothing else has any use for it.
    /// </summary>
    private const string COOKIE_PATH = "/login";

    /// <summary>
    /// Long enough to survive the redirect back, short enough not to linger.
    /// </summary>
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Write the rejected address.
    /// </summary>
    /// <param name="response"></param>
    /// <param name="email"></param>
    /// <param name="secure"></param>
    public static void Set(HttpResponse response, string email, bool secure) =>
        response.Cookies.Append(Name, email, new CookieOptions
        {
            HttpOnly = true,
            Secure = secure,
            SameSite = SameSiteMode.Lax,
            Path = COOKIE_PATH,
            MaxAge = Lifetime,
            IsEssential = true,
        });

    /// <summary>
    /// Read the rejected address, if one is set.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var email) && !string.IsNullOrWhiteSpace(email)
            ? email
            : null;

    /// <summary>
    /// Drop the cookie once the message has been shown.
    /// </summary>
    /// <param name="response"></param>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Delete(Name, new CookieOptions { Path = COOKIE_PATH });
}
