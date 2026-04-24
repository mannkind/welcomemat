using System;
using System.Security.Cryptography;
using System.Text;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to refer to an email address in the logs
/// without writing it down.
/// </summary>
public static class EmailFingerprint
{
    /// <summary>
    /// Mask the local part, keeping enough to recognize an address you know.
    /// </summary>
    /// <param name="email"></param>
    /// <returns></returns>
    public static string Mask(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "(empty)";
        }

        var at = email.LastIndexOf('@');
        if (at <= 0 || at == email.Length - 1)
        {
            return "***";
        }

        var local = email[..at];
        var domain = email[(at + 1)..];

        // Short locals reveal too much from any prefix, so show none of them.
        var (head, tail) = local.Length switch
        {
            <= 4 => (0, 0),
            <= 8 => (2, 1),
            _ => (3, 2),
        };
        var hidden = new string('*', local.Length - head - tail);
        return $"{local[..head]}{hidden}{local[^tail..]}@{domain}";
    }

    /// <summary>
    /// Hash an address so two log lines can be tied to the same person.
    /// </summary>
    /// <param name="email"></param>
    /// <returns></returns>
    public static string Sha256Hex(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email)));
}
