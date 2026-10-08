using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using WelcomeMat.Config;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to redirect to https, but only for the
/// hosts configured as https.
/// </summary>
public class PerHostHttpsRedirectMiddleware
{
    /// <summary>
    /// Initializes a new instance of the PerHostHttpsRedirectMiddleware class.
    /// </summary>
    /// <param name="next"></param>
    public PerHostHttpsRedirectMiddleware(RequestDelegate next)
    {
        this.Next = next;
    }

    /// <summary>
    /// Redirect the request when its host is meant to be https.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="configuration"></param>
    /// <returns></returns>
    public async Task InvokeAsync(HttpContext context, ValidatedAppConfiguration configuration)
    {
        var host = context.Request.Host.Host;
        var matchedPublicUri = configuration.PublicUris.FirstOrDefault(uri =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));

        var isHttpsHost = matchedPublicUri is not null &&
            string.Equals(matchedPublicUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

        // Container health checks arrive over http; redirecting them reports the app as down.
        var isHealthCheck = context.Request.Path == "/healthz";

        if (isHttpsHost && !context.Request.IsHttps && !isHealthCheck)
        {
            var redirectTarget = new UriBuilder(matchedPublicUri!)
            {
                Path = context.Request.Path.Value ?? "/",
            }.Uri;
            var location = redirectTarget.GetLeftPart(UriPartial.Path) + context.Request.QueryString;

            context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = location;
            return;
        }

        await this.Next(context);
    }

    /// <summary>
    /// The next middleware in the pipeline.
    /// </summary>
    private readonly RequestDelegate Next;
}
