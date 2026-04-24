namespace WelcomeMat.Services;

/// <summary>
/// The length and range limits applied to user input across the application.
/// </summary>
public static class InputLimits
{
    /// <summary>
    /// Property name, spot name, path name, wifi network, door code, and friends.
    /// </summary>
    public const int ShortLabelMaxLength = 200;

    /// <summary>
    /// Reservation notes, blackout reasons, guestbook messages.
    /// </summary>
    public const int NoteMaxLength = 4000;

    /// <summary>
    /// The limit for free-form prose; house rules, arrival notes, etc.
    /// </summary>
    public const int LongTextMaxLength = 10000;

    /// <summary>
    /// The longest span a single blackout may cover.
    /// </summary>
    public const int MaxBlackoutSpanDays = 366;

    /// <summary>
    /// A walking path needs two points to be a path.
    /// </summary>
    public const int MinWalkingPathPoints = 2;

    /// <summary>
    /// The most points accepted in a single walking path.
    /// </summary>
    public const int MaxWalkingPathPoints = 2000;

    /// <summary>
    /// The southern limit of a valid latitude.
    /// </summary>
    public const double MinLatitude = -90.0;

    /// <summary>
    /// The northern limit of a valid latitude.
    /// </summary>
    public const double MaxLatitude = 90.0;

    /// <summary>
    /// The western limit of a valid longitude.
    /// </summary>
    public const double MinLongitude = -180.0;

    /// <summary>
    /// The eastern limit of a valid longitude.
    /// </summary>
    public const double MaxLongitude = 180.0;

    /// <summary>
    /// The thinnest a walking path may be drawn.
    /// </summary>
    public const int MinWalkingPathWeight = 1;

    /// <summary>
    /// The thickest a walking path may be drawn.
    /// </summary>
    public const int MaxWalkingPathWeight = 12;

    /// <summary>
    /// A fully transparent walking path.
    /// </summary>
    public const double MinWalkingPathOpacity = 0.0;

    /// <summary>
    /// A fully opaque walking path.
    /// </summary>
    public const double MaxWalkingPathOpacity = 1.0;

    /// <summary>
    /// The fewest consecutive nights a property may allow.
    /// </summary>
    public const int MinMaxConsecutiveNights = 1;

    /// <summary>
    /// The most consecutive nights a property may allow.
    /// </summary>
    public const int MaxMaxConsecutiveNights = 365;

    /// <summary>
    /// The least advance notice a property may require.
    /// </summary>
    public const int MinMinAdvanceNoticeDays = 0;

    /// <summary>
    /// The most advance notice a property may require.
    /// </summary>
    public const int MaxMinAdvanceNoticeDays = 365;

    /// <summary>
    /// The shortest booking window a property may offer.
    /// </summary>
    public const int MinMaxAdvanceBookingDays = 1;

    /// <summary>
    /// The longest booking window a property may offer.
    /// </summary>
    public const int MaxMaxAdvanceBookingDays = 3650;

    /// <summary>
    /// The shortest window in which guest details stay visible.
    /// </summary>
    public const int MinAccessWindowDays = 0;

    /// <summary>
    /// The longest window in which guest details stay visible.
    /// </summary>
    public const int MaxAccessWindowDays = 365;

    /// <summary>
    /// Determine if a short label fits.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsValidShortLabel(string? value) =>
        value is null || value.Length <= ShortLabelMaxLength;

    /// <summary>
    /// Determine if a note fits.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsValidNote(string? value) =>
        value is null || value.Length <= NoteMaxLength;

    /// <summary>
    /// Determine if a block of prose fits.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsValidLongText(string? value) =>
        value is null || value.Length <= LongTextMaxLength;

    /// <summary>
    /// Determine if the value is a #rrggbb color.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static bool IsValidHexColor(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#')
        {
            return false;
        }

        for (var i = 1; i < 7; i++)
        {
            var c = value[i];
            var isHex = c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
            if (!isHex)
            {
                return false;
            }
        }

        return true;
    }
}
