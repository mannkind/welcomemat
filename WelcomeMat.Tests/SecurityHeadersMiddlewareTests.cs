using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using WelcomeMat.Config;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class SecurityHeadersMiddlewareTests
{
    private static IHost BuildHost(
        IReadOnlyList<string> baseUrls,
        IReadOnlyList<System.Net.IPAddress>? trustedProxies = null)
    {
        var appOptions = new AppOptions { BaseUrls = baseUrls };
        var configuration = new ValidatedAppConfiguration(appOptions);

        var builder = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(configuration);
                        services.Configure<ForwardedHeadersOptions>(o =>
                        {
                            o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
                            foreach (var proxy in trustedProxies ?? [])
                            {
                                o.KnownProxies.Add(proxy);
                            }
                        });
                        services.Configure<HostFilteringOptions>(o =>
                        {
                            o.AllowedHosts.Clear();
                            foreach (var h in configuration.AllowedHosts)
                            {
                                o.AllowedHosts.Add(h);
                            }
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseHostFiltering();
                        app.Use(async (ctx, next) =>
                        {
                            ctx.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.9");
                            await next();
                        });
                        app.UseForwardedHeaders();
                        app.UseWelcomeMatSecurityHeaders();
                        app.Run(async ctx =>
                        {
                            await ctx.Response.WriteAsync(ctx.Request.Path == "/healthz" ? "healthy" : "ok");
                        });
                    });
            });

        var host = builder.Start();
        return host;
    }

    [Test]
    public async Task HttpsHost_Response_ContainsAllSecurityHeaders()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Host = "example.com";
        var response = await client.GetAsync("https://example.com/");

        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.True);
        Assert.That(response.Headers.GetValues("X-Content-Type-Options"), Does.Contain("nosniff"));
        Assert.That(response.Headers.GetValues("X-Frame-Options"), Does.Contain("DENY"));
        Assert.That(response.Headers.Contains("Permissions-Policy"), Is.True);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.That(csp, Does.Contain("default-src 'self'"));
        Assert.That(csp, Does.Contain("frame-ancestors 'none'"));
        Assert.That(csp, Does.Contain("'unsafe-eval'"));
        Assert.That(csp, Does.Contain("img-src 'self' data: https://tile.openstreetmap.org"));
        Assert.That(csp, Does.Match(@"'nonce-[A-Za-z0-9+/=]+'"));
    }

    [Test]
    public async Task Csp_Nonce_DiffersAcrossResponses()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();

        var first = await client.GetAsync("https://example.com/");
        var second = await client.GetAsync("https://example.com/");

        var firstCsp = first.Headers.GetValues("Content-Security-Policy").Single();
        var secondCsp = second.Headers.GetValues("Content-Security-Policy").Single();
        Assert.That(firstCsp, Is.Not.EqualTo(secondCsp));
    }

    [Test]
    public async Task HttpRequest_ToHttpsConfiguredHost_RedirectsPermanentlyToExactValidatedOrigin()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "example.com";

        var response = await client.GetAsync("http://example.com/some/path?x=1");

        Assert.That((int)response.StatusCode, Is.EqualTo(308));
        Assert.That(response.Headers.Location!.ToString(), Is.EqualTo("https://example.com/some/path?x=1"));
    }

    [Test]
    public async Task HttpRequest_ToHttpConfiguredHost_InMixedSetup_DoesNotRedirect_AndNoHsts()
    {
        using var host = BuildHost(["https://example.com", "http://internal.local"]);
        var client = host.GetTestClient();

        var response = await client.GetAsync("http://internal.local/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Headers.Contains("Strict-Transport-Security"), Is.False);
    }

    [Test]
    public async Task InvalidHost_Returns400()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();

        var response = await client.GetAsync("https://not-a-configured-host.example/");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task HealthzPath_OverPlainHttp_ToHttpsConfiguredHost_Returns200_NotRedirected()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "example.com";

        var response = await client.GetAsync("http://example.com/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("healthy"));
    }

    [Test]
    public async Task NonHealthzPaths_OverPlainHttp_ToHttpsConfiguredHost_StillRedirect()
    {
        using var host = BuildHost(["https://example.com"]);
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Host = "example.com";

        foreach (var path in new[] { "/", "/healthzzz", "/healthz/nested", "/some/path" })
        {
            var response = await client.GetAsync($"http://example.com{path}");
            Assert.That((int)response.StatusCode, Is.EqualTo(308), $"expected redirect for {path}");
        }
    }

    [Test]
    public async Task UntrustedForwardedHeaders_CannotChangeSchemeOrClientAddress()
    {
        using var host = BuildHost(
            ["https://example.com"],
            trustedProxies: [System.Net.IPAddress.Parse("10.0.0.1")]);
        var client = host.GetTestClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/");
        request.Headers.Add("X-Forwarded-Proto", "https");
        request.Headers.Add("X-Forwarded-For", "203.0.113.9");

        var response = await client.SendAsync(request);

        Assert.That((int)response.StatusCode, Is.EqualTo(308));
    }
}
