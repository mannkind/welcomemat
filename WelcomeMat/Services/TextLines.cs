using System.Collections.Generic;
using System.Linq;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to turn a textarea into lines.
/// </summary>
public static class TextLines
{
    /// <summary>
    /// Split text into trimmed, non-empty lines.
    /// </summary>
    /// <param name="text"></param>
    /// <returns></returns>
    public static IEnumerable<string> Split(string? text) =>
        (text ?? string.Empty)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
}
