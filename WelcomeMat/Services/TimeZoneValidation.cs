using System;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to validate time zone identifiers.
/// </summary>
public static class TimeZoneValidation
{
    /// <summary>
    /// Determine if the id names a time zone this machine knows.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public static bool IsValidIanaId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out _);
}
