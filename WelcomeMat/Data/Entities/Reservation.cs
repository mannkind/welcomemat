using System;
using System.Collections.Generic;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A booked stay. Cancelled stays keep their row and set DeletedAt.
/// </summary>
public class Reservation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid PropertyId { get; set; }
    public DateOnly CheckIn { get; set; }
    public DateOnly CheckOut { get; set; }
    public string Notes { get; set; } = "";
    public DateTime? DeletedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
    public DateTime CreatedAt { get; set; }

    public User? User { get; set; }
    public Property? Property { get; set; }
    public ICollection<GuestbookEntry> GuestbookEntries { get; set; } = [];
}
