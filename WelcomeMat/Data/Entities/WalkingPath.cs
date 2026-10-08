using System;

namespace WelcomeMat.Data.Entities;

/// <summary>
/// A route drawn on a property's map.
/// </summary>
public class WalkingPath
{
    public Guid Id { get; set; }
    public Guid PropertyId { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3388ff";
    public int Weight { get; set; } = 3;
    public double Opacity { get; set; } = 1.0;

    /// <summary>
    /// The path as JSON; [[lat,lng],...].
    /// </summary>
    public string Coordinates { get; set; } = "[]";
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; }
    public Property? Property { get; set; }
}
