using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace WelcomeMat.Pages;

/// <summary>
/// An class representing a way to answer the same handler from a fetch or a
/// full page post.
/// </summary>
public static class AjaxResults
{
    /// <summary>
    /// Determine if the page's script sent this, rather than the browser.
    /// </summary>
    /// <param name="request"></param>
    /// <returns></returns>
    public static bool IsFetch(this HttpRequest request) =>
        request.Headers["X-Requested-With"] == "fetch";

    /// <summary>
    /// Redirect; fetch callers get the target in a header, because XHR follows
    /// a 302 itself.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="url"></param>
    /// <param name="fallback"></param>
    /// <returns></returns>
    public static IActionResult RedirectOrFetch(HttpContext ctx, string url, IActionResult fallback)
    {
        if (ctx.Request.IsFetch())
        {
            ctx.Response.Headers["X-Redirect"] = url;
            return new OkResult();
        }
        return fallback;
    }

    /// <summary>
    /// Report success; a fetch caller only needs the status.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="fallback"></param>
    /// <returns></returns>
    public static IActionResult OkOrFallback(HttpContext ctx, IActionResult fallback) =>
        ctx.Request.IsFetch() ? new OkResult() : fallback;

    /// <summary>
    /// Report a failure; a fetch caller gets the message as plain text to show.
    /// </summary>
    /// <param name="ctx"></param>
    /// <param name="message"></param>
    /// <param name="fallback"></param>
    /// <returns></returns>
    public static IActionResult ErrorOrFallback(HttpContext ctx, string message, IActionResult fallback) =>
        ctx.Request.IsFetch()
            ? new ContentResult { Content = message, StatusCode = StatusCodes.Status400BadRequest, ContentType = "text/plain" }
            : fallback;
}
