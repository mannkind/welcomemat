using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WelcomeMat.Config;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to turn an address into coordinates.
/// </summary>
public class GeocodeService
{
    /// <summary>
    /// Initializes a new instance of the GeocodeService class.
    /// </summary>
    /// <param name="http"></param>
    /// <param name="logger"></param>
    /// <param name="options"></param>
    /// <param name="rateGate"></param>
    /// <param name="cache"></param>
    public GeocodeService(HttpClient http, ILogger<GeocodeService> logger, IOptions<AppOptions> options, GeocodeRateGate rateGate, IMemoryCache cache)
    {
        this.Http = http;
        this.Logger = logger;
        this.Options = options;
        this.RateGate = rateGate;
        this.Cache = cache;
    }

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);
    private const string DEFAULT_PROVIDER_HOST = "nominatim.openstreetmap.org";

    public const string CacheKey = "geocode";

    public const int MaxCachedAddresses = 10_000;

    public static readonly TimeSpan CacheDuration = TimeSpan.FromDays(30);

    /// <summary>
    /// Look up an address; null when the provider has nothing or is refusing.
    /// </summary>
    /// <param name="Lat"></param>
    /// <param name="ct"></param>
    /// <returns></returns>
    public async Task<(double Lat, double Lon)?> GeocodeAsync(string address, CancellationToken ct = default)
    {
        var normalized = Normalize(address);
        if (normalized is null)
        {
            return null;
        }

        var opts = this.Options.Value;
        if (!opts.GeocodingEnabled || opts.GeocodingBaseUrl is null)
        {
            return null;
        }

        var baseUri = opts.GeocodingBaseUrl;
        var cacheKey = (baseUri.AbsoluteUri, normalized);
        if (this.Cache.TryGetValue<(double Lat, double Lon)>(cacheKey, out var cached))
        {
            return cached;
        }

        if (string.Equals(baseUri.Host, DEFAULT_PROVIDER_HOST, StringComparison.OrdinalIgnoreCase))
        {
            if (!await this.RateGate.WaitTurnAsync(ct).ConfigureAwait(false))
            {
                this.Logger.LogWarning("Geocoding skipped: the provider rate-limit queue is full.");
                return null;
            }
        }

        var url = new Uri(baseUri, $"search?q={Uri.EscapeDataString(normalized)}&format=json&limit=1");
        try
        {
            var results = await this.Http.GetFromJsonAsync<NominatimResult[]>(url, JsonOpts, ct).ConfigureAwait(false);
            if (results is { Length: > 0 } &&
                double.TryParse(results[0].Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(results[0].Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            {
                var coords = (lat, lon);
                this.Cache.Set(cacheKey, coords, new MemoryCacheEntryOptions
                {
                    Size = 1,
                    AbsoluteExpirationRelativeToNow = CacheDuration,
                });
                return coords;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            this.Logger.LogWarning(ex, "Geocoding request failed.");
        }
        return null;
    }

    /// <summary>
    /// Collapse an address to its cache key.
    /// </summary>
    /// <param name="address"></param>
    /// <returns></returns>
    private static string? Normalize(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        return string.Join(
            ' ',
            address.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private record NominatimResult
    {
        public string Lat { get; init; } = string.Empty;

        public string Lon { get; init; } = string.Empty;
    }

    /// <summary>
    /// The client used to reach the source.
    /// </summary>
    private readonly HttpClient Http;

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<GeocodeService> Logger;

    /// <summary>
    /// The application options.
    /// </summary>
    private readonly IOptions<AppOptions> Options;

    /// <summary>
    /// The gate used to stay inside Nominatim's rate limit.
    /// </summary>
    private readonly GeocodeRateGate RateGate;

    /// <summary>
    /// The internal cache.
    /// </summary>
    private readonly IMemoryCache Cache;
}
