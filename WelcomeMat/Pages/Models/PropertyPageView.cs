using System;

namespace WelcomeMat.Pages.Models;

/// <summary>
/// The property fields the property page renders.
/// </summary>
public record PropertyPageView
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Information { get; init; } = string.Empty;

    public string CheckoutProcedures { get; init; } = string.Empty;

    public bool IsActive { get; init; }

    public double? Latitude { get; init; }

    public double? Longitude { get; init; }

    public string TimeZone { get; init; } = string.Empty;
}
