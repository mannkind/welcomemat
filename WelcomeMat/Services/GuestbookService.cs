using System;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing the rules around signing the guestbook.
/// </summary>
public static class GuestbookService
{
    /// <summary>
    /// Determine if a stay can still be signed for; opens the day before
    /// checkout and closes two weeks after.
    /// </summary>
    /// <param name="checkOut"></param>
    /// <param name="today"></param>
    /// <returns></returns>
    public static bool IsInWriteWindow(DateOnly checkOut, DateOnly today)
        => today >= checkOut.AddDays(-1) && today <= checkOut.AddDays(14);
}
