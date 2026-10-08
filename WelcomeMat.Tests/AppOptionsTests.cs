using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using WelcomeMat.Config;

namespace WelcomeMat.Tests;

public class AppOptionsTests
{
    [Test]
    public void Production_AcceptsCompleteConfiguration() =>
        Assert.That(ValidOptions().Validate("Production"), Is.Empty);

    [TestCase(true)]
    [TestCase(false)]
    public void Production_RejectsSpecifiedTestAuth(bool enabled)
    {
        var options = ValidOptions() with
        {
            TestAuthWasSpecified = true,
            TestAuthEnabled = enabled,
        };

        Assert.That(options.Validate("Production"), Does.Contain(
            "WELCOMEMAT_TEST_AUTH is only valid in Testing."));
    }

    [TestCase("true", true)]
    [TestCase("TRUE", true)]
    [TestCase("1", true)]
    [TestCase("false", false)]
    [TestCase("FALSE", false)]
    [TestCase("0", false)]
    public void Loader_AcceptsDocumentedTestAuthValues(string value, bool expected)
    {
        var options = AppOptionsLoader.Load(
            EnvironmentWith(("WELCOMEMAT_TEST_AUTH", value)),
            "Testing");

        Assert.Multiple(() =>
        {
            Assert.That(options.TestAuthWasSpecified, Is.True);
            Assert.That(options.TestAuthEnabled, Is.EqualTo(expected));
            Assert.That(options.Validate("Testing"), Is.Empty);
        });
    }

    [TestCase("true")]
    [TestCase("false")]
    [TestCase("1")]
    [TestCase("0")]
    public void Loader_ProductionRejectsAnySpecifiedTestAuthValue(string value)
    {
        var options = AppOptionsLoader.Load(
            EnvironmentWith(("WELCOMEMAT_TEST_AUTH", value)),
            "Production");

        Assert.That(options.Validate("Production"), Does.Contain(
            "WELCOMEMAT_TEST_AUTH is only valid in Testing."));
    }

    [TestCase("Production")]
    [TestCase("Testing")]
    public void Loader_RejectsMalformedTestAuthInEveryEnvironment(string environmentName)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppOptionsLoader.Load(
                EnvironmentWith(("WELCOMEMAT_TEST_AUTH", "sometimes")),
                environmentName));

        Assert.That(exception!.Message, Is.EqualTo(
            "WELCOMEMAT_TEST_AUTH must be one of true, false, 1, or 0."));
    }

    [Test]
    public void Production_RejectsMissingGoogleCredentials()
    {
        var errors = (ValidOptions() with
        {
            GoogleClientId = "",
            GoogleClientSecret = "",
        }).Validate("Production");

        Assert.Multiple(() =>
        {
            Assert.That(errors, Does.Contain("GOOGLE_CLIENT_ID is required in Production."));
            Assert.That(errors, Does.Contain("GOOGLE_CLIENT_SECRET is required in Production."));
        });
    }

    [TestCase("")]
    [TestCase("not-a-url")]
    [TestCase("ftp://welcome.example.com")]
    [TestCase("https://user:password@welcome.example.com")]
    [TestCase("https://welcome.example.com/search?q=value")]
    [TestCase("https://welcome.example.com/#fragment")]
    [TestCase("https://welcome.example.com/calendar")]
    [TestCase("https:///")]
    public void Validation_RejectsInvalidBaseUrl(string baseUrl)
    {
        var errors = (ValidOptions() with { BaseUrls = [baseUrl] }).Validate("Production");

        Assert.That(errors, Does.Contain(
            "BASE_URL entries must be absolute HTTP(S) origins without credentials, query, fragment, or a non-root path."));
    }

    [Test]
    public void Validation_RejectsEmptyBaseUrlList()
    {
        var errors = (ValidOptions() with { BaseUrls = [] }).Validate("Production");

        Assert.That(errors, Does.Contain(
            "BASE_URL must contain at least one HTTP(S) URL."));
    }

    [Test]
    public void Validation_RejectsDuplicateHostWithConflictingSchemes()
    {
        var errors = (ValidOptions() with
        {
            BaseUrls =
            [
                "http://welcome.example.com",
                "https://welcome.example.com",
            ],
        }).Validate("Production");

        Assert.That(errors, Does.Contain(
            "BASE_URL cannot configure the same host with conflicting schemes."));
    }

    [TestCase("")]
    [TestCase("localhost")]
    [TestCase("localhost:not-a-port")]
    [TestCase(":0")]
    [TestCase(":65536")]
    public void Validation_RejectsInvalidListenAddress(string listenAddress)
    {
        var errors = (ValidOptions() with { ListenAddr = listenAddress }).Validate("Production");

        Assert.That(errors, Does.Contain(
            "LISTEN_ADDR must be :port or host:port with a port from 1 through 65535."));
    }

    [Test]
    public void Loader_RejectsInvalidTrustedProxyIp()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppOptionsLoader.Load(
                EnvironmentWith(("TRUSTED_PROXIES", "127.0.0.1,not-an-ip")),
                "Production"));

        Assert.That(exception!.Message, Is.EqualTo(
            "TRUSTED_PROXIES contains an invalid IP address: not-an-ip."));
    }

    [Test]
    public void Loader_RejectsInvalidTrustedNetworkCidr()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            AppOptionsLoader.Load(
                EnvironmentWith(("TRUSTED_NETWORKS", "10.0.0.0/8,broken")),
                "Production"));

        Assert.That(exception!.Message, Is.EqualTo(
            "TRUSTED_NETWORKS contains an invalid CIDR network: broken."));
    }

    [Test]
    public void Loader_ParsesConfiguredNetworkValues()
    {
        var options = AppOptionsLoader.Load(
            EnvironmentWith(
                ("TRUSTED_PROXIES", "192.0.2.10,2001:db8::10"),
                ("TRUSTED_NETWORKS", "198.51.100.0/24,2001:db8::/32")),
            "Production");

        Assert.Multiple(() =>
        {
            Assert.That(options.TrustedProxies, Is.EqualTo(new[]
            {
                IPAddress.Parse("192.0.2.10"),
                IPAddress.Parse("2001:db8::10"),
            }));
            Assert.That(options.TrustedNetworks.Select(n => n.ToString()), Is.EqualTo(new[]
            {
                "198.51.100.0/24",
                "2001:db8::/32",
            }));
        });
    }

    [Test]
    public void ValidatedConfiguration_ParsesPublicOriginsAndAllowedHosts()
    {
        var validated = new ValidatedAppConfiguration(ValidOptions() with
        {
            BaseUrls =
            [
                "https://welcome.example.com",
                "http://localhost:8080",
            ],
        });

        Assert.Multiple(() =>
        {
            Assert.That(validated.PublicUris.Select(u => u.AbsoluteUri), Is.EqualTo(new[]
            {
                "https://welcome.example.com/",
                "http://localhost:8080/",
            }));
            Assert.That(validated.AllowedHosts, Is.EqualTo(new[]
            {
                "welcome.example.com",
                "localhost",
            }));
        });
    }

    [Test]
    public void ValidatedConfiguration_DefaultsTrustedNetworks_WhenNeitherIsConfigured() =>
        Assert.That(
            new ValidatedAppConfiguration(ValidOptions()).TrustedNetworks.Select(n => n.ToString()),
            Is.EqualTo(new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16" }));

    [Test]
    public void ValidatedConfiguration_DoesNotDefaultTrustedNetworks_WhenTrustedProxiesConfigured() =>
        Assert.That(
            new ValidatedAppConfiguration(ValidOptions() with
            {
                TrustedProxies = [IPAddress.Parse("192.0.2.10")],
            }).TrustedNetworks,
            Is.Empty);

    [Test]
    public void ValidatedConfiguration_UsesConfiguredTrustedNetworks_WhenSet() =>
        Assert.That(
            new ValidatedAppConfiguration(ValidOptions() with
            {
                TrustedNetworks = [IPNetwork.Parse("198.51.100.0/24")],
            }).TrustedNetworks.Select(n => n.ToString()),
            Is.EqualTo(new[] { "198.51.100.0/24" }));

    [Test]
    public void UseSecureCookies_True_WhenEveryBaseUrlIsHttps() =>
        Assert.That(
            (ValidOptions() with
            {
                BaseUrls = ["https://a.example.com", "https://b.example.com"],
            }).UseSecureCookies,
            Is.True);

    [Test]
    public void UseSecureCookies_False_WhenBaseUrlsAreMixed() =>
        Assert.That(
            (ValidOptions() with
            {
                BaseUrls = ["https://welcome.example.com", "http://localhost:8080"],
            }).UseSecureCookies,
            Is.False);

    private static IServiceProvider BuildOptionsProvider(
        string environmentName, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s =>
                new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IValidateOptions<AppOptions>>(new ValidateAppOptions(environmentName));
        services.AddOptions<AppOptions>()
            .Configure<IConfiguration>(AppOptionsLoader.Populate)
            .ValidateOnStart();
        return services.BuildServiceProvider();
    }

    [Test]
    public void OptionsPipeline_BindsFlatConfigurationKeys()
    {
        var provider = BuildOptionsProvider(
            "Production",
            ("BASE_URL", "https://welcome.example.com"),
            ("GOOGLE_CLIENT_ID", "google-client-id"),
            ("GOOGLE_CLIENT_SECRET", "google-client-secret"),
            ("LISTEN_ADDR", ":9090"));

        var options = provider.GetRequiredService<IOptions<AppOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.BaseUrls, Is.EqualTo(new[] { "https://welcome.example.com" }));
            Assert.That(options.GoogleClientId, Is.EqualTo("google-client-id"));
            Assert.That(options.ListenAddr, Is.EqualTo(":9090"));
        });
    }

    [Test]
    public void OptionsPipeline_AcceptsValuesFromAnyConfigurationSource()
    {
        var provider = BuildOptionsProvider(
            "Production",
            ("BASE_URL", "https://from-appsettings.example.com"),
            ("GOOGLE_CLIENT_ID", "id"),
            ("GOOGLE_CLIENT_SECRET", "secret"));

        var options = provider.GetRequiredService<IOptions<AppOptions>>().Value;

        Assert.That(options.BaseUrls, Is.EqualTo(new[] { "https://from-appsettings.example.com" }));
    }

    [Test]
    public void OptionsPipeline_RejectsInvalidConfiguration_WithEveryErrorAtOnce()
    {
        var provider = BuildOptionsProvider(
            "Production",
            ("BASE_URL", "https://welcome.example.com"),
            ("LISTEN_ADDR", "not-a-port"));

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<AppOptions>>().Value);

        Assert.That(exception!.Failures, Is.EquivalentTo(new[]
        {
            "GOOGLE_CLIENT_ID is required in Production.",
            "GOOGLE_CLIENT_SECRET is required in Production.",
            "LISTEN_ADDR must be :port or host:port with a port from 1 through 65535.",
        }));
    }

    [Test]
    public void OptionsPipeline_AllowsTestAuth_OnlyInTestingEnvironment()
    {
        var testing = BuildOptionsProvider(
            "Testing",
            ("BASE_URL", "https://welcome.example.com"),
            ("WELCOMEMAT_TEST_AUTH", "1"));
        Assert.That(testing.GetRequiredService<IOptions<AppOptions>>().Value.TestAuthEnabled, Is.True);

        var production = BuildOptionsProvider(
            "Production",
            ("BASE_URL", "https://welcome.example.com"),
            ("GOOGLE_CLIENT_ID", "id"),
            ("GOOGLE_CLIENT_SECRET", "secret"),
            ("WELCOMEMAT_TEST_AUTH", "1"));

        var exception = Assert.Throws<OptionsValidationException>(
            () => _ = production.GetRequiredService<IOptions<AppOptions>>().Value);
        Assert.That(exception!.Failures, Does.Contain("WELCOMEMAT_TEST_AUTH is only valid in Testing."));
    }

    private static AppOptions ValidOptions() => new()
    {
        GoogleClientId = "google-client-id",
        GoogleClientSecret = "google-client-secret",
        BaseUrls = ["https://welcome.example.com"],
        ListenAddr = ":8080",
    };

    private static Func<string, string?> EnvironmentWith(
        params (string Key, string Value)[] overrides)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASE_URL"] = "https://welcome.example.com",
            ["GOOGLE_CLIENT_ID"] = "google-client-id",
            ["GOOGLE_CLIENT_SECRET"] = "google-client-secret",
            ["LISTEN_ADDR"] = ":8080",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return key => values.GetValueOrDefault(key);
    }
}
