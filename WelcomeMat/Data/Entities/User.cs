using System;
using System.Collections.Generic;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// Someone who has signed in.
/// </summary>
public class User
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public string Name { get; set; } = "";
    public string Picture { get; set; } = "";
    public bool IsAdmin { get; set; }
    public string AuthProvider { get; set; } = "oidc";

    /// <summary>
    /// The secret in the user's personal ical feed URL.
    /// </summary>
    public string CalendarToken { get; set; } = "";

    /// <summary>
    /// Bump this to invalidate every cookie already issued to the user.
    /// </summary>
    public long SecurityVersion { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = [];
    public ICollection<AllowlistEntry> AllowlistEntries { get; set; } = [];
    public ICollection<GuestDoorCode> GuestDoorCodes { get; set; } = [];
    public ICollection<GuestbookEntry> GuestbookEntries { get; set; } = [];
}
