using System;
using System.IO;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WelcomeMat.Authentication;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Pages.Api;
using WelcomeMat.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables();

var optionsValidator = new ValidateAppOptions(builder.Environment.EnvironmentName);

builder.Services.AddSingleton<IValidateOptions<AppOptions>>(optionsValidator);
builder.Services.AddOptions<AppOptions>()
    .Configure<IConfiguration>(AppOptionsLoader.Populate)
    .ValidateOnStart();

var appOpts = AppOptionsLoader.Load(builder.Configuration, builder.Environment.EnvironmentName);
var eagerValidation = optionsValidator.Validate(Options.DefaultName, appOpts);
if (eagerValidation.Failed)
{
    throw new OptionsValidationException(
        Options.DefaultName, typeof(AppOptions), eagerValidation.Failures);
}
var validatedConfiguration = new ValidatedAppConfiguration(appOpts);

Directory.CreateDirectory(appOpts.StatePath);
Directory.CreateDirectory(appOpts.ImagesPath);
Directory.CreateDirectory(appOpts.DataProtectionKeysPath);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(appOpts.DatabasePath)) ?? appOpts.StatePath);

builder.Services.AddSingleton(validatedConfiguration);
builder.Services.AddSingleton<PublicUrlService>();
builder.Services.AddSingleton(BuildTimeProvider(appOpts, builder.Environment.EnvironmentName));
builder.Services.AddSingleton<PropertyClock>();
builder.Services.AddDataProtection()
    .SetApplicationName("WelcomeMat")
    .PersistKeysToFileSystem(new DirectoryInfo(appOpts.DataProtectionKeysPath));

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor;
    foreach (var proxy in validatedConfiguration.TrustedProxies)
    {
        o.KnownProxies.Add(proxy);
    }

    foreach (var network in validatedConfiguration.TrustedNetworks)
    {
        o.KnownIPNetworks.Add(network);
    }
});
builder.Services.Configure<HostFilteringOptions>(o =>
{
    o.AllowedHosts.Clear();
    foreach (var host in validatedConfiguration.AllowedHosts)
    {
        o.AllowedHosts.Add(host);
    }
});

var cookieSecurePolicy = appOpts.UseSecureCookies
    ? CookieSecurePolicy.Always
    : CookieSecurePolicy.SameAsRequest;

var connStr = SqliteConfiguration.ConnectionString(appOpts);
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(connStr));

builder.Services.AddSingleton<SqliteTicketStore>();
builder.Services.AddScoped<SessionValidationService>();
builder.Services.AddScoped<BootstrapAdminService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = cookieSecurePolicy;
        o.ExpireTimeSpan = TimeSpan.FromDays(7);
        o.SlidingExpiration = false;
    })
    .AddOpenIdConnect("Google", o =>
    {
        o.Authority = "https://accounts.google.com";
        o.ClientId = appOpts.GoogleClientId;
        o.ClientSecret = appOpts.GoogleClientSecret;
        o.CallbackPath = "/auth/google/callback";
        o.ResponseType = OpenIdConnectResponseType.Code;
        o.ResponseMode = OpenIdConnectResponseMode.Query;
        o.Scope.Clear();
        o.Scope.Add("openid");
        o.Scope.Add("email");
        o.Scope.Add("profile");
        o.SaveTokens = false;
        o.GetClaimsFromUserInfoEndpoint = true;

        o.CorrelationCookie.SecurePolicy = cookieSecurePolicy;
        o.CorrelationCookie.SameSite = SameSiteMode.Lax;
        o.NonceCookie.SecurePolicy = cookieSecurePolicy;
        o.NonceCookie.SameSite = SameSiteMode.Lax;

        o.EventsType = typeof(GoogleOidcEvents);
    });
builder.Services.AddScoped<GoogleOidcEvents>();

builder.Services.AddAuthorization(o =>
    o.AddPolicy("AdminOnly", p => p.RequireClaim("is_admin", "true")));

builder.Services.AddAntiforgery(o => o.HeaderName = "X-XSRF-TOKEN");

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Login");
    o.Conventions.AllowAnonymousToPage("/Logout");
    o.Conventions.AuthorizeFolder("/Admin", "AdminOnly");
});

builder.Services.AddSingleton<GeocodeRateGate>();
builder.Services.AddKeyedSingleton<IMemoryCache>(
    GeocodeService.CacheKey,
    (_, _) => new MemoryCache(new MemoryCacheOptions { SizeLimit = GeocodeService.MaxCachedAddresses }));
builder.Services.AddHttpClient<GeocodeService>(c =>
    c.DefaultRequestHeaders.UserAgent.ParseAdd("WelcomeMat/1.0"));
builder.Services.AddHttpClient<IPushoverService, PushoverService>(c =>
    c.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddScoped<IReservationRepository, EfReservationRepository>();
builder.Services.AddScoped<ReservationService>();
builder.Services.AddScoped<ReservationCoordinator>();
builder.Services.AddScoped<PropertyAccessService>();
builder.Services.AddScoped<StayTimesService>();
builder.Services.AddScoped<ReservationCalendarService>();
builder.Services.AddSingleton<PropertyImageService>();
builder.Services.AddHostedService<MigrationHostedService>();

var app = builder.Build();

var ticketStore = app.Services.GetRequiredService<SqliteTicketStore>();
app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
    .Get(CookieAuthenticationDefaults.AuthenticationScheme).SessionStore = ticketStore;

app.UseHostFiltering();
app.UseForwardedHeaders();
app.UseWelcomeMatSecurityHeaders();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapRazorPages();
app.MapHealthEndpoints();
app.MapReservationEndpoints();
app.MapIcalEndpoints();
app.MapImpersonationEndpoints();
app.MapPropertyImageEndpoints();

if (appOpts.TestAuthEnabled && app.Environment.IsEnvironment("Testing"))
{
    app.Logger.LogWarning(
        "WELCOMEMAT_TEST_AUTH is enabled: unauthenticated /test-auth and /test-support endpoints are active. This must NEVER be set in production.");
    app.MapTestSupportEndpoints();
}

app.Run(appOpts.GetListenUrl());

static TimeProvider BuildTimeProvider(AppOptions options, string environmentName)
{
    if (options.TestAuthEnabled && environmentName == "Testing")
    {
        var frozen = Environment.GetEnvironmentVariable("WELCOMEMAT_TEST_FROZEN_UTC");
        if (!string.IsNullOrEmpty(frozen) &&
            DateTimeOffset.TryParse(
                frozen,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return new FrozenTimeProvider(parsed);
        }
    }
    return TimeProvider.System;
}
