using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace WelcomeMat.Config;

/// <summary>
/// An class representing the configuration after it has been validated, so
/// nothing downstream has to re-parse or re-check it.
/// </summary>
public class ValidatedAppConfiguration
{
    /// <summary>
    /// The RFC1918 ranges, trusted when nothing else is configured.
    /// </summary>
    private static readonly IReadOnlyList<IPNetwork> DefaultTrustedNetworks =
    [
        IPNetwork.Parse("10.0.0.0/8"),
        IPNetwork.Parse("172.16.0.0/12"),
        IPNetwork.Parse("192.168.0.0/16"),
    ];

    /// <summary>
    /// The origins this application answers on.
    /// </summary>
    public IReadOnlyList<Uri> PublicUris { get; }

    /// <summary>
    /// The hosts from PublicUris, deduplicated.
    /// </summary>
    public IReadOnlyList<string> AllowedHosts { get; }

    /// <summary>
    /// The proxies whose forwarded headers are believed.
    /// </summary>
    public IReadOnlyList<IPAddress> TrustedProxies { get; }

    /// <summary>
    /// The networks whose forwarded headers are believed.
    /// </summary>
    public IReadOnlyList<IPNetwork> TrustedNetworks { get; }

    /// <summary>
    /// A flag that indicates if every configured origin is https.
    /// </summary>
    public bool UseSecureCookies { get; }

    /// <summary>
    /// Initializes a new instance of the ValidatedAppConfiguration class.
    /// </summary>
    /// <param name="options"></param>
    public ValidatedAppConfiguration(AppOptions options)
    {
        this.UseSecureCookies = options.UseSecureCookies;
        this.PublicUris = options.BaseUrls
            .Select(value =>
                AppOptions.TryParsePublicOrigin(value, out var uri)
                    ? uri
                    : throw new InvalidOperationException($"Invalid BASE_URL: {value}"))
            .ToArray();
        this.AllowedHosts = this.PublicUris
            .Select(uri => uri.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        this.TrustedProxies = options.TrustedProxies;
        this.TrustedNetworks = options.TrustedProxies.Count == 0 && options.TrustedNetworks.Count == 0
            ? DefaultTrustedNetworks
            : options.TrustedNetworks;
    }
}
