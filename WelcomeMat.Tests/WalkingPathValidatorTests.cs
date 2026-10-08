using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class WalkingPathValidatorTests
{
    [Test]
    public void Null_or_whitespace_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse(null, out _, out var error), Is.False);
        Assert.That(error, Is.Not.Empty);
        Assert.That(WalkingPathValidator.TryParse("   ", out _, out _), Is.False);
    }

    [Test]
    public void Malformed_json_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[1,2],", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("valid JSON"));
    }

    [Test]
    public void Scalar_json_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("42", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("array"));

        Assert.That(WalkingPathValidator.TryParse("\"hello\"", out _, out _), Is.False);
        Assert.That(WalkingPathValidator.TryParse("{}", out _, out _), Is.False);
    }

    [Test]
    public void Nan_and_infinity_are_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[1e400,2],[3,4]]", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("finite"));
    }

    [Test]
    public void Out_of_range_latitude_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[91,0],[0,0]]", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("Latitude"));

        Assert.That(WalkingPathValidator.TryParse("[[-91,0],[0,0]]", out _, out _), Is.False);
    }

    [Test]
    public void Out_of_range_longitude_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[0,181],[0,0]]", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("Longitude"));

        Assert.That(WalkingPathValidator.TryParse("[[0,-181],[0,0]]", out _, out _), Is.False);
    }

    [Test]
    public void One_point_is_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[1,2]]", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("at least"));
    }

    [Test]
    public void Two_points_is_the_minimum_valid_count()
    {
        Assert.That(WalkingPathValidator.TryParse("[[1,2],[3,4]]", out var points, out _), Is.True);
        Assert.That(points, Has.Count.EqualTo(2));
        Assert.That(points[0], Is.EqualTo(new GeoPoint { Latitude = 1, Longitude = 2 }));
    }

    [Test]
    public void Two_thousand_points_is_the_maximum_valid_count()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 2000).Select(i => "[0,0]")) + "]";
        Assert.That(WalkingPathValidator.TryParse(json, out var points, out _), Is.True);
        Assert.That(points, Has.Count.EqualTo(2000));
    }

    [Test]
    public void Two_thousand_and_one_points_is_rejected()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 2001).Select(i => "[0,0]")) + "]";
        Assert.That(WalkingPathValidator.TryParse(json, out _, out var error), Is.False);
        Assert.That(error, Does.Contain("at most"));
    }

    [Test]
    public void Non_pair_elements_are_rejected()
    {
        Assert.That(WalkingPathValidator.TryParse("[[1,2,3],[4,5]]", out _, out var error), Is.False);
        Assert.That(error, Does.Contain("pair"));

        Assert.That(WalkingPathValidator.TryParse("[[1],[4,5]]", out _, out _), Is.False);
        Assert.That(WalkingPathValidator.TryParse("[1,[4,5]]", out _, out _), Is.False);
    }

    [Test]
    public void ToJson_round_trips_through_invariant_culture()
    {
        var points = new List<GeoPoint>
        {
            new() { Latitude = 34.0195, Longitude = -118.4912 },
            new() { Latitude = 0, Longitude = 0 },
        };
        var json = WalkingPathValidator.ToJson(points);
        Assert.That(WalkingPathValidator.TryParse(json, out var reparsed, out _), Is.True);
        Assert.That(reparsed, Is.EqualTo(points));
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(12, true)]
    [TestCase(13, false)]
    [TestCase(-1, false)]
    public void Weight_bounds_are_enforced(int weight, bool expected) =>
        Assert.That(WalkingPathValidator.IsValidWeight(weight), Is.EqualTo(expected));

    [TestCase(-0.01, false)]
    [TestCase(0.0, true)]
    [TestCase(1.0, true)]
    [TestCase(1.01, false)]
    public void Opacity_bounds_are_enforced(double opacity, bool expected) =>
        Assert.That(WalkingPathValidator.IsValidOpacity(opacity), Is.EqualTo(expected));

    [Test]
    public void Opacity_nan_and_infinity_are_rejected()
    {
        Assert.That(WalkingPathValidator.IsValidOpacity(double.NaN), Is.False);
        Assert.That(WalkingPathValidator.IsValidOpacity(double.PositiveInfinity), Is.False);
        Assert.That(WalkingPathValidator.IsValidOpacity(double.NegativeInfinity), Is.False);
    }

    [TestCase("#3388ff", true)]
    [TestCase("#3388FF", true)]
    [TestCase("3388ff", false)]
    [TestCase("#3388f", false)]
    [TestCase("#3388ffa", false)]
    [TestCase("#gggggg", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void Color_format_is_enforced(string? color, bool expected) =>
        Assert.That(InputLimits.IsValidHexColor(color), Is.EqualTo(expected));
}
