using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Services;

/// <summary>
/// An class representing a managed way to re-check a session on every request.
/// </summary>
public class SessionValidationService
{
    /// <summary>
    /// Initializes a new instance of the SessionValidationService class.
    /// </summary>
    /// <param name="db"></param>
    /// <param name="clock"></param>
    public SessionValidationService(AppDbContext db, TimeProvider clock)
    {
        this.Db = db;
        this.Clock = clock;
    }

    /// <summary>
    /// Rebuild the ticket from the current user row; null means stop trusting
    /// this session.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="ticket"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<AuthenticationTicket?> ValidateAndRebuildAsync(
        Session session, AuthenticationTicket ticket, CancellationToken cancellationToken)
    {
        var user = await this.LoadValidUserAsync(session, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var principal = ticket.Principal;
        var impersonatorId =
            Guid.TryParse(principal.FindFirst(AuthClaims.ImpersonatorIdClaim)?.Value, out var id)
                ? id
                : (Guid?)null;
        var impersonatorName = principal.FindFirst(AuthClaims.ImpersonatorNameClaim)?.Value;

        var rebuiltPrincipal = AuthClaims.BuildPrincipal(user, impersonatorId, impersonatorName);
        return new AuthenticationTicket(rebuiltPrincipal, ticket.Properties, ticket.AuthenticationScheme);
    }

    /// <summary>
    /// Drop sessions that no longer validate, a page at a time.
    /// </summary>
    /// <param name="maximumRows"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<int> CleanupInvalidAsync(int maximumRows, CancellationToken cancellationToken)
    {
        var candidates = await this.Db.Sessions
            .OrderBy(s => s.Token)
            .Take(maximumRows)
            .ToListAsync(cancellationToken);

        var invalid = new List<Session>();
        foreach (var session in candidates)
        {
            if (await this.LoadValidUserAsync(session, cancellationToken) is null)
            {
                invalid.Add(session);
            }
        }

        if (invalid.Count == 0)
        {
            return 0;
        }

        this.Db.Sessions.RemoveRange(invalid);
        await this.Db.SaveChangesAsync(cancellationToken);
        return invalid.Count;
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;

    /// <summary>
    /// The clock used to check expiry.
    /// </summary>
    private readonly TimeProvider Clock;

    /// <summary>
    /// The user behind a session, or null for any of the reasons a session dies:
    /// expired, no user, a bumped security version, or off the allowlist.
    /// </summary>
    /// <param name="session"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<User?> LoadValidUserAsync(Session session, CancellationToken cancellationToken)
    {
        if (session.Expiry < ToUnixSeconds(this.Clock.GetUtcNow()))
        {
            return null;
        }

        if (session.UserId is not { } userId)
        {
            return null;
        }

        var user = await this.Db.Users.FindAsync([userId], cancellationToken);
        if (user is null)
        {
            return null;
        }

        if (user.SecurityVersion != session.SecurityVersion)
        {
            return null;
        }

        var stillAllowed = await this.Db.Allowlist.AnyAsync(a => a.Email == user.Email, cancellationToken);
        if (!stillAllowed)
        {
            return null;
        }

        return user;
    }

    /// <summary>
    /// Sessions store expiry as fractional unix seconds.
    /// </summary>
    /// <param name="dto"></param>
    /// <returns></returns>
    private static double ToUnixSeconds(DateTimeOffset dto) => dto.ToUnixTimeMilliseconds() / 1000.0;
}
