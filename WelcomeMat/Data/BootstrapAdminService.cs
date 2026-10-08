using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Data;

/// <summary>
/// An class representing a way to seed the first admin, once, from INITIAL_USER.
/// </summary>
public class BootstrapAdminService
{
    /// <summary>
    /// Initializes a new instance of the BootstrapAdminService class.
    /// </summary>
    /// <param name="db"></param>
    public BootstrapAdminService(AppDbContext db)
    {
        this.Db = db;
    }

    private const string BOOTSTRAP_MARKER_KEY = "initial_admin_bootstrap_completed";

    /// <summary>
    /// Seed the first admin from INITIAL_USER. Runs once; a marker row keeps
    /// a restart from re-promoting someone who was demoted.
    /// </summary>
    /// <param name="initialUser"></param>
    /// <param name="requireInitialAdministrator"></param>
    /// <param name="cancellationToken"></param>
    public async Task BootstrapAsync(
        string? initialUser, bool requireInitialAdministrator, CancellationToken cancellationToken)
    {
        var alreadyBootstrapped = await this.Db.Settings
            .AnyAsync(s => s.Key == BOOTSTRAP_MARKER_KEY, cancellationToken);
        if (alreadyBootstrapped)
        {
            return;
        }

        var hasExistingState =
            await this.Db.Users.AnyAsync(cancellationToken) || await this.Db.Allowlist.AnyAsync(cancellationToken);
        if (hasExistingState)
        {
            await this.MarkBootstrappedAsync(cancellationToken);
            return;
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(initialUser)
            ? null
            : EmailNormalizer.Normalize(initialUser);

        if (normalizedEmail is null)
        {
            if (requireInitialAdministrator)
            {
                throw new InvalidOperationException(
                    "INITIAL_USER is required to bootstrap a new installation: set INITIAL_USER " +
                    "to the email address of the first administrator.");
            }

            return;
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            Name = normalizedEmail,
            IsAdmin = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await using var transaction = await this.Db.Database.BeginTransactionAsync(cancellationToken);
        this.Db.Users.Add(user);
        this.Db.Allowlist.Add(new AllowlistEntry
        {
            Id = Guid.NewGuid(),
            Email = normalizedEmail,
            AddedBy = user.Id,
            CreatedAt = now,
        });
        this.Db.Settings.Add(new Setting { Key = BOOTSTRAP_MARKER_KEY, Value = "true" });
        await this.Db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Record that bootstrap ran, so a restart does not repeat it.
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task MarkBootstrappedAsync(CancellationToken cancellationToken)
    {
        this.Db.Settings.Add(new Setting { Key = BOOTSTRAP_MARKER_KEY, Value = "true" });
        await this.Db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
