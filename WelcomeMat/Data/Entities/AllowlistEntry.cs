using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// An email allowed to sign in. No row, no access.
/// </summary>
public class AllowlistEntry
{
    public Guid Id { get; set; }
    public required string Email { get; set; }
    public Guid? AddedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public User? AddedByUser { get; set; }
}
