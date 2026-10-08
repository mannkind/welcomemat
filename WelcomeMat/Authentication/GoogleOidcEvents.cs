using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Logging;
using WelcomeMat.Config;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Authentication;

/// <summary>
/// An class representing the hooks into the Google sign-in handshake, where the
/// allowlist is enforced and the user row is created.
/// </summary>
public class GoogleOidcEvents : OpenIdConnectEvents
{
    /// <summary>
    /// Initializes a new instance of the GoogleOidcEvents class.
    /// </summary>
    /// <param name="logger"></param>
    /// <param name="publicUrls"></param>
    /// <param name="config"></param>
    /// <param name="db"></param>
    public GoogleOidcEvents(ILogger<GoogleOidcEvents> logger, PublicUrlService publicUrls, ValidatedAppConfiguration config, AppDbContext db)
    {
        this.Logger = logger;
        this.PublicUrls = publicUrls;
        this.Config = config;
        this.Db = db;
    }

    public const string SelectAccountItem = "wm:select_account";

    /// <inheritdoc />
    public override Task RedirectToIdentityProvider(RedirectContext ctx)
    {
        ctx.ProtocolMessage.RedirectUri = this.PublicUrls.Build(
            "/auth/google/callback",
            ctx.HttpContext.Request.Host);
        if (ctx.Properties.Items.ContainsKey(SelectAccountItem))
        {
            ctx.ProtocolMessage.Prompt = "select_account";
        }

        this.Logger.LogDebug("OIDC: redirecting to identity provider.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task MessageReceived(MessageReceivedContext ctx)
    {
        this.Logger.LogDebug("OIDC: callback message received.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override async Task TicketReceived(TicketReceivedContext ctx)
    {
        this.Logger.LogDebug("OIDC: ticket received.");

        var rawEmail = ctx.Principal?.FindFirstValue(ClaimTypes.Email)
            ?? ctx.Principal?.FindFirstValue("email");

        if (string.IsNullOrEmpty(rawEmail))
        {
            this.Logger.LogWarning("OIDC: ticket rejected, no email claim present.");
            ctx.Fail("No email in token.");
            return;
        }

        var email = EmailNormalizer.Normalize(rawEmail);

        var allowed = this.Db.Allowlist.Any(a => a.Email == email);
        if (!allowed)
        {
            this.Logger.LogWarning(
                "OIDC: ticket rejected, email not on allowlist. Masked: {MaskedEmail} SHA256: {EmailHash}",
                EmailFingerprint.Mask(email),
                EmailFingerprint.Sha256Hex(email));
            RejectedLoginCookie.Set(
                ctx.Response,
                email,
                this.Config.UseSecureCookies || ctx.Request.IsHttps);
            ctx.Response.Redirect("/login?error=not_allowed");
            ctx.HandleResponse();
            return;
        }

        var name = ctx.Principal?.FindFirstValue(ClaimTypes.Name)
            ?? ctx.Principal?.FindFirstValue("name") ?? email;
        var picture = ctx.Principal?.FindFirstValue("picture") ?? "";

        var user = this.Db.Users.SingleOrDefault(u => u.Email == email);
        if (user is null)
        {
            user = new User
            {
                Email = email,
                Name = name,
                Picture = picture,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };
            this.Db.Users.Add(user);
        }
        else
        {
            user.Name = name;
            user.Picture = picture;
            user.UpdatedAt = DateTime.UtcNow;
        }
        if (string.IsNullOrEmpty(user.CalendarToken))
        {
            user.CalendarToken = Guid.NewGuid().ToString("N");
        }

        await this.Db.SaveChangesAsync(ctx.HttpContext.RequestAborted);

        ctx.Principal = AuthClaims.BuildPrincipal(user);
        ctx.Properties!.RedirectUri ??= "/calendar";
        this.Logger.LogDebug("OIDC: ticket accepted.");
    }

    /// <inheritdoc />
    public override Task RemoteFailure(RemoteFailureContext ctx)
    {
        this.Logger.LogWarning(
            "OIDC: remote authentication failed. Exception type: {ExceptionType}",
            ctx.Failure?.GetType().Name ?? "Unknown");
        ctx.Response.Redirect("/login?error=auth_failed");
        ctx.HandleResponse();
        return Task.CompletedTask;
    }

    /// <summary>
    /// The logger used internally.
    /// </summary>
    private readonly ILogger<GoogleOidcEvents> Logger;

    /// <summary>
    /// The service used to build externally reachable URLs.
    /// </summary>
    private readonly PublicUrlService PublicUrls;

    /// <summary>
    /// The validated configuration.
    /// </summary>
    private readonly ValidatedAppConfiguration Config;

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
