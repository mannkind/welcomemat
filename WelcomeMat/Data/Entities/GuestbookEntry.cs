using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// What a guest wrote about their stay.
/// </summary>
public class GuestbookEntry
{
    public Guid Id { get; set; }
    public Guid ReservationId { get; set; }
    public Guid PropertyId { get; set; }
    public Guid UserId { get; set; }
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public Reservation? Reservation { get; set; }
    public Property? Property { get; set; }
    public User? User { get; set; }
}
