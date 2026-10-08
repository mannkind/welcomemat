using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using Microsoft.Extensions.Configuration;

namespace WelcomeMat.Config;

/// <summary>
/// An class representing a way to read the options out of the environment.
/// </summary>
public static class AppOptionsLoader
{
    public const string DefaultGeocodingBaseUrl = "https://nominatim.openstreetmap.org/";

    /// <summary>
    /// Read the options out of the environment.
    /// </summary>
    /// <param name="configuration"></param>
    /// <param name="environmentName"></param>
    /// <returns></returns>
    public static AppOptions Load(IConfiguration configuration, string environmentName) =>
        Load(key => configuration[key], environmentName);

    public static AppOptions Load(Func<string, string?> getEnv, string environmentName)
    {
        var options = new AppOptions();
        Populate(options, getEnv);
        return options;
    }

    /// <summary>
    /// Fill an existing options instance from the environment.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="configuration"></param>
    public static void Populate(AppOptions options, IConfiguration configuration) =>
        Populate(options, key => configuration[key]);

    /// <summary>
    /// Fill the options from a lookup, so tests need no IConfiguration.
    /// </summary>
    /// <param name="options"></param>
    /// <param name="getEnv"></param>
    private static void Populate(AppOptions options, Func<string, string?> getEnv)
    {
        var statePath = NonEmpty(getEnv("STATE_PATH")) ?? "./state";
        var databasePath = NonEmpty(getEnv("DATABASE_PATH")) ?? Path.Combine(statePath, "reservations.db");
        var (testAuthWasSpecified, testAuthEnabled) = ParseTestAuth(getEnv("WELCOMEMAT_TEST_AUTH"));
        var baseUrls = SplitList(getEnv("BASE_URL"));

        options.GoogleClientId = getEnv("GOOGLE_CLIENT_ID") ?? "";
        options.GoogleClientSecret = getEnv("GOOGLE_CLIENT_SECRET") ?? "";
        options.BaseUrls = baseUrls.Length > 0 ? baseUrls : ["http://localhost:8080"];
        options.StatePath = statePath;
        options.DatabasePath = databasePath;
        options.ListenAddr = NonEmpty(getEnv("LISTEN_ADDR")) ?? ":8080";
        options.InitialUser = NonEmpty(getEnv("INITIAL_USER"));
        options.PushoverTokens = SplitList(getEnv("PUSHOVER_TOKEN"));
        options.PushoverUserTokens = SplitList(getEnv("PUSHOVER_USER_TOKEN"));
        options.GeocodingBaseUrl = new Uri(NonEmpty(getEnv("GEOCODING_BASE_URL")) ?? DefaultGeocodingBaseUrl);
        options.GeocodingEnabled = bool.TryParse(getEnv("GEOCODING_ENABLED"), out var geocodingEnabled) ? geocodingEnabled : true;
        options.TrustedProxies = ParseIpAddresses(getEnv("TRUSTED_PROXIES"));
        options.TrustedNetworks = ParseIpNetworks(getEnv("TRUSTED_NETWORKS"));
        options.TestAuthWasSpecified = testAuthWasSpecified;
        options.TestAuthEnabled = testAuthEnabled;
    }

    /// <summary>
    /// Read WELCOMEMAT_TEST_AUTH; whether it was set is its own answer.
    /// </summary>
    /// <param name="WasSpecified"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private static (bool WasSpecified, bool Enabled) ParseTestAuth(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return (false, false);
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" => (true, true),
            "false" or "0" => (true, false),
            _ => throw new InvalidOperationException(
                "WELCOMEMAT_TEST_AUTH must be one of true, false, 1, or 0."),
        };
    }

    /// <summary>
    /// Parse a comma-separated list of proxy addresses.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static IReadOnlyList<IPAddress> ParseIpAddresses(string? value)
    {
        var addresses = new List<IPAddress>();
        foreach (var entry in SplitList(value))
        {
            if (!IPAddress.TryParse(entry, out var address))
            {
                throw new InvalidOperationException(
                    $"TRUSTED_PROXIES contains an invalid IP address: {entry}.");
            }

            addresses.Add(address);
        }
        return addresses;
    }

    /// <summary>
    /// Parse a comma-separated list of CIDR networks.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static IReadOnlyList<IPNetwork> ParseIpNetworks(string? value)
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in SplitList(value))
        {
            if (!IPNetwork.TryParse(entry, out var network))
            {
                throw new InvalidOperationException(
                    $"TRUSTED_NETWORKS contains an invalid CIDR network: {entry}.");
            }

            networks.Add(network);
        }
        return networks;
    }

    private static string[] SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string? NonEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
