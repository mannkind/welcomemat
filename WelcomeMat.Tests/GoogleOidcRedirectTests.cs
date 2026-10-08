using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NUnit.Framework;
using WelcomeMat.Authentication;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class GoogleOidcRedirectTests
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

    [Test]
    public async Task RedirectToIdentityProvider_WithSelectAccountItem_AsksGoogleForTheChooser()
    {
        var ctx = BuildRedirectContext(selectAccount: true);

        await this.BuildEvents().RedirectToIdentityProvider(ctx);

        Assert.That(ctx.ProtocolMessage.Prompt, Is.EqualTo("select_account"));
    }

    [Test]
    public async Task RedirectToIdentityProvider_ByDefault_DoesNotForceTheChooser()
    {
        var ctx = BuildRedirectContext(selectAccount: false);

        await this.BuildEvents().RedirectToIdentityProvider(ctx);

        Assert.That(ctx.ProtocolMessage.Prompt, Is.Null);
    }

    [Test]
    public async Task RedirectToIdentityProvider_AlwaysUsesThePublicCallbackUrl()
    {
        var ctx = BuildRedirectContext(selectAccount: true);

        await this.BuildEvents().RedirectToIdentityProvider(ctx);

        Assert.That(ctx.ProtocolMessage.RedirectUri, Is.EqualTo("https://example.com/auth/google/callback"));
    }

    private GoogleOidcEvents BuildEvents()
    {
        var config = new ValidatedAppConfiguration(new AppOptions { BaseUrls = ["https://example.com"] });
        return new GoogleOidcEvents(
            NullLogger<GoogleOidcEvents>.Instance,
            new PublicUrlService(config),
            config,
            this.Db);
    }

    private static RedirectContext BuildRedirectContext(bool selectAccount)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        http.Request.Host = new HostString("example.com");

        var properties = new AuthenticationProperties();
        if (selectAccount)
        {
            properties.Items[GoogleOidcEvents.SelectAccountItem] = "true";
        }

        var scheme = new AuthenticationScheme("Google", "Google", typeof(OpenIdConnectHandler));
        return new RedirectContext(http, scheme, new OpenIdConnectOptions(), properties)
        {
            ProtocolMessage = new OpenIdConnectMessage(),
        };
    }
}
