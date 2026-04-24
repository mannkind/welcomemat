using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A date nobody may book. A null PropertyId blacks out every property.
/// </summary>
public class BlackoutDate
{
    public Guid Id { get; set; }
    public Guid? PropertyId { get; set; }

    /// <summary>
    /// Stored as yyyy-MM-dd text, because SQLite has no date type.
    /// </summary>
    public required string Date { get; set; }
    public string Reason { get; set; } = "";
    public Guid CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }

    public Property? Property { get; set; }
    public User? CreatedByUser { get; set; }
}
