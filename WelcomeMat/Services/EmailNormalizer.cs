namespace WelcomeMat.Services;

/// <summary>
/// An class representing a way to normalize email addresses.
/// </summary>
public static class EmailNormalizer
{
    /// <summary>
    /// Normalize an email address for comparison and storage.
    /// </summary>
    /// <param name="email"></param>
    /// <returns></returns>
    public static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
