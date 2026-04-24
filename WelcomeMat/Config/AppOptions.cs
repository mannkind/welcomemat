using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;

namespace WelcomeMat.Config;

/// <summary>
/// The application options, as read from the environment.
/// </summary>
public record AppOptions
{
    private const string INVALID_BASE_URL_MESSAGE =
        "BASE_URL entries must be absolute HTTP(S) origins without credentials, query, fragment, or a non-root path.";

    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public IReadOnlyList<string> BaseUrls { get; set; } = ["http://localhost:8080"];
    public string StatePath { get; set; } = "./state";
    public string ImagesPath => Path.Combine(this.StatePath, "images");
    public string DataProtectionKeysPath => Path.Combine(this.StatePath, "data-protection-keys");
    public string DatabasePath { get; set; } = "./state/reservations.db";
    public string ListenAddr { get; set; } = ":8080";
    public string? InitialUser { get; set; }
    public string[] PushoverTokens { get; set; } = [];
    public string[] PushoverUserTokens { get; set; } = [];
    public Uri? GeocodingBaseUrl { get; set; }
    public bool GeocodingEnabled { get; set; } = true;
    public IReadOnlyList<IPAddress> TrustedProxies { get; set; } = [];
    public IReadOnlyList<IPNetwork> TrustedNetworks { get; set; } = [];

    public bool TestAuthWasSpecified { get; set; }
    public bool TestAuthEnabled { get; set; }

    public bool UseSecureCookies =>
        this.BaseUrls.Count > 0 &&
        this.BaseUrls.All(u => Uri.TryCreate(u, UriKind.Absolute, out var uri) &&
                          uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Check the options; the returned messages are what startup prints.
    /// </summary>
    /// <param name="environmentName"></param>
    /// <returns></returns>
    public IReadOnlyList<string> Validate(string environmentName)
    {
        var errors = new List<string>();
        var publicUris = new List<Uri>();

        if (this.BaseUrls.Count == 0)
        {
            errors.Add("BASE_URL must contain at least one HTTP(S) URL.");
        }
        else
        {
            foreach (var value in this.BaseUrls)
            {
                if (!TryParsePublicOrigin(value, out var uri))
                {
                    errors.Add(INVALID_BASE_URL_MESSAGE);
                    continue;
                }
                publicUris.Add(uri);
            }

            if (publicUris
                .GroupBy(uri => uri.Host, StringComparer.OrdinalIgnoreCase)
                .Any(group => group
                    .Select(uri => uri.Scheme)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Skip(1)
                    .Any()))
            {
                errors.Add("BASE_URL cannot configure the same host with conflicting schemes.");
            }
        }

        if (!TryParseListenAddress(this.ListenAddr))
        {
            errors.Add(
                "LISTEN_ADDR must be :port or host:port with a port from 1 through 65535.");
        }

        if (environmentName.Equals("Production", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(this.GoogleClientId))
            {
                errors.Add("GOOGLE_CLIENT_ID is required in Production.");
            }

            if (string.IsNullOrWhiteSpace(this.GoogleClientSecret))
            {
                errors.Add("GOOGLE_CLIENT_SECRET is required in Production.");
            }
        }

        if (this.TestAuthWasSpecified &&
            !environmentName.Equals("Testing", StringComparison.Ordinal))
        {
            errors.Add("WELCOMEMAT_TEST_AUTH is only valid in Testing.");
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Parse a BASE_URL entry; an origin only, no credentials, query, or path.
    /// </summary>
    /// <param name="value"></param>
    /// <param name="uri"></param>
    /// <returns></returns>
    internal static bool TryParsePublicOrigin(string value, out Uri uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(parsed.Host) ||
            !string.IsNullOrEmpty(parsed.UserInfo) ||
            !string.IsNullOrEmpty(parsed.Query) ||
            !string.IsNullOrEmpty(parsed.Fragment) ||
            parsed.AbsolutePath != "/")
        {
            uri = null!;
            return false;
        }

        uri = parsed;
        return true;
    }

    /// <summary>
    /// Parse :port, host:port, or [v6]:port.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static bool TryParseListenAddress(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string portText;
        if (value.StartsWith(':'))
        {
            portText = value[1..];
        }
        else if (value.StartsWith('['))
        {
            var separator = value.LastIndexOf("]:", StringComparison.Ordinal);
            if (separator <= 1)
            {
                return false;
            }

            portText = value[(separator + 2)..];
        }
        else
        {
            var separator = value.LastIndexOf(':');
            if (separator <= 0 || separator == value.Length - 1)
            {
                return false;
            }

            portText = value[(separator + 1)..];
        }

        return int.TryParse(portText, out var port) && port is >= 1 and <= 65535;
    }

    /// <summary>
    /// The url Kestrel binds, from LISTEN_ADDR.
    /// </summary>
    /// <returns></returns>
    public string GetListenUrl() =>
        this.ListenAddr.StartsWith(':')
            ? $"http://*{this.ListenAddr}"
            : $"http://{this.ListenAddr}";
}
