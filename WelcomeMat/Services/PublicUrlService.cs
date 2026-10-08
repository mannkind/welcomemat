using System;
using System.Linq;
using Microsoft.AspNetCore.Http;
using WelcomeMat.Config;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to build absolute, externally reachable URLs.
/// </summary>
public class PublicUrlService
{
    /// <summary>
    /// Initializes a new instance of the PublicUrlService class.
    /// </summary>
    /// <param name="configuration"></param>
    public PublicUrlService(ValidatedAppConfiguration configuration)
    {
        this.Configuration = configuration;
    }

    /// <summary>
    /// Build an absolute https URL for the path.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="requestHost"></param>
    /// <returns></returns>
    public string Build(string path, HostString? requestHost = null)
    {
        var origin = this.SelectOrigin(requestHost);
        return new Uri(origin, NormalizePath(path)).AbsoluteUri;
    }

    /// <summary>
    /// Build a webcal:// URL so calendar clients subscribe rather than download.
    /// </summary>
    /// <param name="path"></param>
    /// <param name="requestHost"></param>
    /// <returns></returns>
    public string BuildWebcal(string path, HostString? requestHost = null)
    {
        var origin = this.SelectOrigin(requestHost);
        return $"webcal://{origin.Authority}{NormalizePath(path)}";
    }

    /// <summary>
    /// The validated configuration holding the public origins.
    /// </summary>
    private readonly ValidatedAppConfiguration Configuration;

    /// <summary>
    /// Pick the configured origin matching the request; the first one otherwise.
    /// </summary>
    /// <param name="requestHost"></param>
    /// <returns></returns>
    private Uri SelectOrigin(HostString? requestHost)
    {
        if (requestHost is { } host)
        {
            var match = this.Configuration.PublicUris.FirstOrDefault(uri =>
                uri.Host.Equals(host.Host, StringComparison.OrdinalIgnoreCase) &&
                (host.Port is null || uri.Port == host.Port));
            if (match is not null)
            {
                return match;
            }
        }

        return this.Configuration.PublicUris[0];
    }

    /// <summary>
    /// Ensure the path is rooted.
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    private static string NormalizePath(string path) =>
        path.StartsWith('/') ? path : $"/{path}";
}
