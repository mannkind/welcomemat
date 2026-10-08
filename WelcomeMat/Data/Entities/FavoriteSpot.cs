using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A place worth visiting near a property, shown on its map.
/// </summary>
public class FavoriteSpot
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public required string Name { get; set; }
    public string Address { get; set; } = "";
    public string Description { get; set; } = "";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }

    public Property? Property { get; set; }
}
