using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class PublicUrlServiceTests
{
    [Test]
    public void Build_UsesConfiguredOriginMatchingValidatedRequestHost()
    {
        var service = CreateService(
            "https://first.example.com",
            "http://second.example.com:8080");

        var result = service.Build(
            "/reservations?view=upcoming",
            new HostString("second.example.com", 8080));

        Assert.That(result, Is.EqualTo(
            "http://second.example.com:8080/reservations?view=upcoming"));
    }

    [Test]
    public void Build_IgnoresUnvalidatedRequestHost()
    {
        var service = CreateService("https://welcome.example.com");

        var result = service.Build(
            "/auth/google/callback",
            new HostString("attacker.example"));

        Assert.That(result, Is.EqualTo(
            "https://welcome.example.com/auth/google/callback"));
    }

    [Test]
    public void Build_NeverUsesARequestScheme()
    {
        var service = CreateService("https://welcome.example.com");

        var result = service.Build("/reservations", new HostString("welcome.example.com"));

        Assert.That(result, Is.EqualTo("https://welcome.example.com/reservations"));
    }

    [Test]
    public void BuildWebcal_UsesConfiguredHostAndPort()
    {
        var service = CreateService("https://welcome.example.com:8443");

        var result = service.BuildWebcal("/ical/calendar-token");

        Assert.That(result, Is.EqualTo(
            "webcal://welcome.example.com:8443/ical/calendar-token"));
    }

    private static PublicUrlService CreateService(params string[] baseUrls)
    {
        var options = new AppOptions
        {
            BaseUrls = baseUrls,
            GoogleClientId = "google-client-id",
            GoogleClientSecret = "google-client-secret",
        };
        return new PublicUrlService(new ValidatedAppConfiguration(options));
    }
}
