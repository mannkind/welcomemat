using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A door code issued to one guest at one property, overriding the
/// property-wide code.
/// </summary>
public class GuestDoorCode
{
    public Guid UserId { get; set; }
    public Guid PropertyId { get; set; }
    public string DoorCode { get; set; } = "";

    public User? User { get; set; }
    public Property? Property { get; set; }
}
