using System;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using WelcomeMat.Authentication;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class SensitiveLoggingTests
{
    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .Options);
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    private static (HttpContext http, AuthenticationScheme scheme, OpenIdConnectOptions options) BuildContext()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };
        http.Request.Host = new HostString("example.com");
        var options = new OpenIdConnectOptions();
        var scheme = new AuthenticationScheme("Google", "Google", typeof(OpenIdConnectHandler));
        return (http, scheme, options);
    }

    [Test]
    public async Task TicketReceived_EmailNotAllowlisted_DoesNotLogEmailOrClaims()
    {
        const string secretEmail = "super-secret-guest@example.com";
        var logger = new CapturingLogger<GoogleOidcEvents>();
        var events = new GoogleOidcEvents(logger, BuildPublicUrlService(), BuildConfiguration(), this.Db);

        var (http, scheme, options) = BuildContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, secretEmail)], "test");
        var principal = new ClaimsPrincipal(identity);
        var props = new AuthenticationProperties();
        var ticket = new AuthenticationTicket(principal, props, "Google");

        var ctx = new TicketReceivedContext(http, scheme, options, ticket);

        await events.TicketReceived(ctx);

        var text = logger.AllText();
        Assert.That(text, Does.Not.Contain(secretEmail));
        Assert.That(text, Does.Not.Contain(ClaimTypes.Email));
        Assert.That(text, Does.Contain(EmailFingerprint.Mask(secretEmail)));
        Assert.That(text, Does.Contain(EmailFingerprint.Sha256Hex(secretEmail)));
    }

    [Test]
    public async Task TicketReceived_EmailNotAllowlisted_HandsTheAddressBackToTheUserInACookie()
    {
        const string secretEmail = "super-secret-guest@example.com";
        var logger = new CapturingLogger<GoogleOidcEvents>();
        var events = new GoogleOidcEvents(logger, BuildPublicUrlService(), BuildConfiguration(), this.Db);

        var (http, scheme, options) = BuildContext();
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, secretEmail)], "test");
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties(), "Google");

        await events.TicketReceived(new TicketReceivedContext(http, scheme, options, ticket));

        var setCookie = http.Response.Headers.SetCookie.ToString();
        Assert.That(setCookie, Does.Contain($"{RejectedLoginCookie.Name}={Uri.EscapeDataString(secretEmail)}"));
        Assert.That(setCookie, Does.Contain("httponly").IgnoreCase);
        Assert.That(setCookie, Does.Contain("path=/login").IgnoreCase);
        Assert.That(http.Response.Headers.Location.ToString(), Does.Not.Contain("secret"));
    }

    [Test]
    public async Task RemoteFailure_DoesNotLogExceptionMessage()
    {
        const string sensitiveMessage = "state=abc123&redirect_uri=https://example.com/secret-callback";
        var logger = new CapturingLogger<GoogleOidcEvents>();
        var events = new GoogleOidcEvents(logger, BuildPublicUrlService(), BuildConfiguration(), this.Db);

        var (http, scheme, options) = BuildContext();
        var ctx = new RemoteFailureContext(http, scheme, options, new InvalidOperationException(sensitiveMessage));

        await events.RemoteFailure(ctx);

        var text = logger.AllText();
        Assert.That(text, Does.Not.Contain(sensitiveMessage));
        Assert.That(text, Does.Not.Contain("state=abc123"));
        Assert.That(text, Does.Contain(nameof(InvalidOperationException)));
    }

    [Test]
    public async Task Pushover_FailedSend_DoesNotLogTokenOrUserOrMessage()
    {
        const string token = "secret-app-token";
        const string userToken = "secret-user-token";
        const string message = "Reservation for the Smith family at 42 Ocean Ave";

        var logger = new CapturingLogger<PushoverService>();
        var handler = new ThrowingHttpMessageHandler();
        var http = new HttpClient(handler);
        var opts = Options.Create(new AppOptions
        {
            PushoverTokens = [token],
            PushoverUserTokens = [userToken],
        });
        var service = new PushoverService(http, opts, logger);

        await service.SendAsync(message);

        var text = logger.AllText();
        Assert.That(text, Does.Not.Contain(token));
        Assert.That(text, Does.Not.Contain(userToken));
        Assert.That(text, Does.Not.Contain(message));
    }

    private static PublicUrlService BuildPublicUrlService() => new(BuildConfiguration());

    private static ValidatedAppConfiguration BuildConfiguration() =>
        new(new AppOptions { BaseUrls = ["https://example.com"] });

    private class ThrowingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("connection refused to api.pushover.net");
    }
}
