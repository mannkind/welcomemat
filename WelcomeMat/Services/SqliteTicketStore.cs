using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.DependencyInjection;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to keep auth cookies server-side, so a
/// session can be revoked.
/// </summary>
public class SqliteTicketStore : ITicketStore
{
    /// <summary>
    /// Initializes a new instance of the SqliteTicketStore class.
    /// </summary>
    /// <param name="services"></param>
    public SqliteTicketStore(IServiceProvider services)
    {
        this.Services = services;
    }

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    /// <inheritdoc />
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var (userId, securityVersion) = OwnershipClaims(ticket);

        var issuedAt = DateTimeOffset.UtcNow;
        var expiresAt = issuedAt + Lifetime;
        ticket.Properties.IssuedUtc = issuedAt;
        ticket.Properties.ExpiresUtc = expiresAt;

        var token = Guid.NewGuid().ToString("N");
        var data = TicketSerializer.Default.Serialize(ticket);

        await using var scope = this.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Sessions.Add(new Session
        {
            Token = token,
            Data = data,
            Expiry = ToUnixSeconds(expiresAt),
            UserId = userId,
            SecurityVersion = securityVersion,
            IssuedAt = ToUnixSeconds(issuedAt),
        });
        await db.SaveChangesAsync();
        return token;
    }

    /// <inheritdoc />
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        await using var scope = this.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.Sessions.FindAsync(key);
        if (session is null)
        {
            return null;
        }

        var ticket = TicketSerializer.Default.Deserialize(session.Data);
        if (ticket is null)
        {
            await RemoveRowAsync(db, session);
            return null;
        }

        var validator = scope.ServiceProvider.GetRequiredService<SessionValidationService>();
        var rebuilt = await validator.ValidateAndRebuildAsync(session, ticket, CancellationToken.None);
        if (rebuilt is null)
        {
            await RemoveRowAsync(db, session);
            return null;
        }

        return rebuilt;
    }

    /// <inheritdoc />
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var (userId, securityVersion) = OwnershipClaims(ticket);

        await using var scope = this.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.Sessions.FindAsync(key);
        if (session is null)
        {
            return;
        }

        ticket.Properties.IssuedUtc = FromUnixSeconds(session.IssuedAt);
        ticket.Properties.ExpiresUtc = FromUnixSeconds(session.Expiry);
        session.Data = TicketSerializer.Default.Serialize(ticket);
        session.UserId = userId;
        session.SecurityVersion = securityVersion;
        await db.SaveChangesAsync();
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key)
    {
        await using var scope = this.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.Sessions.FindAsync(key);
        if (session is not null)
        {
            await RemoveRowAsync(db, session);
        }
    }

    /// <summary>
    /// The user id and security version the session row is keyed on.
    /// </summary>
    /// <param name="UserId"></param>
    /// <param name="ticket"></param>
    /// <returns></returns>
    private static (Guid UserId, long SecurityVersion) OwnershipClaims(AuthenticationTicket ticket)
    {
        var principal = ticket.Principal;
        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            throw new InvalidOperationException("Cannot store a session without a user id claim.");
        }

        var securityVersionClaim = principal.FindFirstValue(AuthClaims.SecurityVersionClaim);
        if (!long.TryParse(securityVersionClaim, out var securityVersion))
        {
            throw new InvalidOperationException("Cannot store a session without a security-version claim.");
        }

        return (userId, securityVersion);
    }

    /// <summary>
    /// Delete a session row.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="session"></param>
    private static async Task RemoveRowAsync(AppDbContext db, Session session)
    {
        db.Sessions.Remove(session);
        await db.SaveChangesAsync();
    }

    private static double ToUnixSeconds(DateTimeOffset dto) =>
        dto.ToUnixTimeMilliseconds() / 1000.0;

    private static DateTimeOffset FromUnixSeconds(double seconds) =>
        DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000.0));

    /// <summary>
    /// The provider used to resolve a scoped database.
    /// </summary>
    private readonly IServiceProvider Services;
}
