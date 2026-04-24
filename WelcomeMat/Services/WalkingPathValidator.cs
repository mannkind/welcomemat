using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WelcomeMat.Services;

/// <summary>
/// A single point on a walking path.
/// </summary>
public readonly record struct GeoPoint
{
    public double Latitude { get; init; }

    public double Longitude { get; init; }
}

/// <summary>
/// An class representing a way to validate the coordinates drawn for a walking path.
/// </summary>
public static class WalkingPathValidator
{
    /// <summary>
    /// Parse the coordinate JSON the map editor posts; false with a reason to show.
    /// </summary>
    /// <param name="json"></param>
    /// <param name="points"></param>
    /// <param name="error"></param>
    /// <returns></returns>
    public static bool TryParse(string? json, out IReadOnlyList<GeoPoint> points, out string error)
    {
        points = [];
        error = "";

        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Path coordinates are required.";
            return false;
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "Path coordinates must be valid JSON.";
            return false;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Array)
            {
                error = "Path coordinates must be a JSON array of [latitude, longitude] pairs.";
                return false;
            }

            var count = root.GetArrayLength();
            if (count < InputLimits.MinWalkingPathPoints)
            {
                error = $"A walking path needs at least {InputLimits.MinWalkingPathPoints} points.";
                return false;
            }

            if (count > InputLimits.MaxWalkingPathPoints)
            {
                error = $"A walking path may have at most {InputLimits.MaxWalkingPathPoints} points.";
                return false;
            }

            var parsed = new List<GeoPoint>(count);
            foreach (var element in root.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2)
                {
                    error = "Each path point must be a [latitude, longitude] pair.";
                    return false;
                }

                var coords = element.EnumerateArray().ToArray();
                if (!TryGetFiniteDouble(coords[0], out var lat) || !TryGetFiniteDouble(coords[1], out var lon))
                {
                    error = "Path coordinates must be finite numbers.";
                    return false;
                }

                if (lat < InputLimits.MinLatitude || lat > InputLimits.MaxLatitude)
                {
                    error = $"Latitude must be between {InputLimits.MinLatitude} and {InputLimits.MaxLatitude}.";
                    return false;
                }

                if (lon < InputLimits.MinLongitude || lon > InputLimits.MaxLongitude)
                {
                    error = $"Longitude must be between {InputLimits.MinLongitude} and {InputLimits.MaxLongitude}.";
                    return false;
                }

                parsed.Add(new GeoPoint { Latitude = lat, Longitude = lon });
            }

            points = parsed;
            return true;
        }
    }

    /// <summary>
    /// Serialize points back to the [latitude, longitude] pairs leaflet wants.
    /// </summary>
    /// <param name="points"></param>
    /// <returns></returns>
    public static string ToJson(IReadOnlyList<GeoPoint> points) =>
        JsonSerializer.Serialize(points.Select(p => new[] { p.Latitude, p.Longitude }));

    /// <summary>
    /// Determine if the stroke width is in range.
    /// </summary>
    /// <param name="weight"></param>
    /// <returns></returns>
    public static bool IsValidWeight(int weight) =>
        weight >= InputLimits.MinWalkingPathWeight && weight <= InputLimits.MaxWalkingPathWeight;

    /// <summary>
    /// Determine if the opacity is in range.
    /// </summary>
    /// <param name="opacity"></param>
    /// <returns></returns>
    public static bool IsValidOpacity(double opacity) =>
        !double.IsNaN(opacity) && !double.IsInfinity(opacity)
        && opacity >= InputLimits.MinWalkingPathOpacity && opacity <= InputLimits.MaxWalkingPathOpacity;

    /// <summary>
    /// Read a JSON number, rejecting NaN and the infinities.
    /// </summary>
    /// <param name="element"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    private static bool TryGetFiniteDouble(JsonElement element, out double value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        if (!element.TryGetDouble(out var d))
        {
            return false;
        }

        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            return false;
        }

        value = d;
        return true;
    }
}
