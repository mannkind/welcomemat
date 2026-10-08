using System;
using System.Collections.Generic;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A place that can be booked.
/// </summary>
public class Property
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string WifiNetwork { get; set; } = "";
    public string WifiPassword { get; set; } = "";
    public string DoorCode { get; set; } = "";

    /// <summary>
    /// The secret in the property's ical feed URL.
    /// </summary>
    public string IcalToken { get; set; } = "";
    public string Information { get; set; } = "";
    public string CheckinProcedures { get; set; } = "";
    public string CheckoutProcedures { get; set; } = "";
    public string TimeZone { get; set; } = "America/Los_Angeles";
    public bool IsActive { get; set; } = true;

    public int? MaxConsecutiveNights { get; set; }
    public int? MinAdvanceNoticeDays { get; set; }
    public int? MaxAdvanceBookingDays { get; set; }

    /// <summary>
    /// How long before check-in the wifi and door code become visible.
    /// </summary>
    public int? AccessWindowDays { get; set; }

    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Reservation> Reservations { get; set; } = [];
    public ICollection<BlackoutDate> BlackoutDates { get; set; } = [];
    public ICollection<FavoriteSpot> FavoriteSpots { get; set; } = [];
    public ICollection<WalkingPath> WalkingPaths { get; set; } = [];
    public ICollection<GuestDoorCode> GuestDoorCodes { get; set; } = [];
    public ICollection<GuestbookEntry> GuestbookEntries { get; set; } = [];
}
