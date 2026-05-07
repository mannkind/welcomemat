using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class TimeZoneValidationTests
{
    [TestCase("America/Los_Angeles")]
    [TestCase("Europe/London")]
    [TestCase("UTC")]
    public void IsValidIanaId_AcceptsKnownZones(string id) =>
        Assert.That(TimeZoneValidation.IsValidIanaId(id), Is.True);

    [TestCase("")]
    [TestCase(null)]
    [TestCase("Not/AZone")]
    [TestCase("not-a-zone-id")]
    public void IsValidIanaId_RejectsInvalidOrEmptyValues(string? id) =>
        Assert.That(TimeZoneValidation.IsValidIanaId(id), Is.False);
}
