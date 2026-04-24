using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class SessionValidationServiceTests
{
    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;
    private ManualTimeProvider Clock = null!;
    private SessionValidationService Validator = null!;

    private class ManualTimeProvider : TimeProvider
    {
        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            this.UtcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => this.UtcNow;


        private readonly DateTimeOffset UtcNow;
    }

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .Options);
        this.Clock = new ManualTimeProvider(DateTimeOffset.UtcNow);
        this.Validator = new SessionValidationService(this.Db, this.Clock);
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    private async Task<User> SeedUserAsync(bool isAdmin = false, bool allowlisted = true, long securityVersion = 0)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{Guid.NewGuid():N}@example.com",
            Name = "Test User",
            IsAdmin = isAdmin,
            SecurityVersion = securityVersion,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        this.Db.Users.Add(user);
        if (allowlisted)
        {
            this.Db.Allowlist.Add(new AllowlistEntry { Id = Guid.NewGuid(), Email = user.Email, CreatedAt = DateTime.UtcNow });
        }

        await this.Db.SaveChangesAsync();
        return user;
    }

    private Session MakeSession(User user, long securityVersion, TimeSpan? expiresIn = null)
    {
        var ticket = TicketFor(user, securityVersion);
        return new Session
        {
            Token = Guid.NewGuid().ToString("N"),
            Data = TicketSerializer.Default.Serialize(ticket),
            UserId = user.Id,
            SecurityVersion = securityVersion,
            IssuedAt = ToUnixSeconds(this.Clock.GetUtcNow()),
            Expiry = ToUnixSeconds(this.Clock.GetUtcNow() + (expiresIn ?? TimeSpan.FromDays(7))),
        };
    }

    private static AuthenticationTicket TicketFor(User user, long securityVersion)
    {
        var stampedUser = new User
        {
            Id = user.Id,
            Email = user.Email,
            Name = user.Name,
            IsAdmin = user.IsAdmin,
            SecurityVersion = securityVersion,
        };
        var principal = AuthClaims.BuildPrincipal(stampedUser);
        return new AuthenticationTicket(principal, "Cookies");
    }

    private static double ToUnixSeconds(DateTimeOffset dto) => dto.ToUnixTimeMilliseconds() / 1000.0;

    [Test]
    public async Task DemotedAdmin_OldTicketIsRejected()
    {
        var admin = await this.SeedUserAsync(isAdmin: true, securityVersion: 0);
        var session = this.MakeSession(admin, securityVersion: 0);
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        admin.IsAdmin = false;
        admin.SecurityVersion++;
        await this.Db.SaveChangesAsync();

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task PromotedUser_OldTicketIsRejected()
    {
        var user = await this.SeedUserAsync(isAdmin: false, securityVersion: 0);
        var session = this.MakeSession(user, securityVersion: 0);
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        user.IsAdmin = true;
        user.SecurityVersion++;
        await this.Db.SaveChangesAsync();

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task RemovedFromAllowlist_SessionIsRejected()
    {
        var user = await this.SeedUserAsync();
        var session = this.MakeSession(user, securityVersion: 0);
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        this.Db.Allowlist.RemoveRange(this.Db.Allowlist.Where(a => a.Email == user.Email));
        await this.Db.SaveChangesAsync();

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task MissingUser_SessionIsRejected()
    {
        var user = await this.SeedUserAsync();
        var session = this.MakeSession(user, securityVersion: 0);
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        this.Db.Users.Remove(user);
        await this.Db.SaveChangesAsync();

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task LegacySessionWithoutUserId_IsRejected()
    {
        var user = await this.SeedUserAsync();
        var session = this.MakeSession(user, securityVersion: 0);
        session.UserId = null; // simulates a row from before the ownership columns existed
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task ExpiredSession_IsRejected()
    {
        var user = await this.SeedUserAsync();
        var session = this.MakeSession(user, securityVersion: 0, expiresIn: TimeSpan.FromSeconds(-1));
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Null);
    }

    [Test]
    public async Task ValidSession_RebuildsPrincipalFromCurrentDatabaseState()
    {
        var user = await this.SeedUserAsync(isAdmin: false);
        var session = this.MakeSession(user, securityVersion: 0);
        var ticket = TicketSerializer.Default.Deserialize(session.Data)!;

        user.Name = "Updated Name";
        await this.Db.SaveChangesAsync();

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(rebuilt!.Principal.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value,
            Is.EqualTo("Updated Name"));
    }

    [Test]
    public async Task ValidSession_PreservesImpersonatorClaims()
    {
        var admin = await this.SeedUserAsync(isAdmin: true);
        var target = await this.SeedUserAsync(isAdmin: false);
        var principal = AuthClaims.BuildPrincipal(target, admin.Id, admin.Name);
        var ticket = new AuthenticationTicket(principal, "Cookies");
        var session = new Session
        {
            Token = Guid.NewGuid().ToString("N"),
            Data = TicketSerializer.Default.Serialize(ticket),
            UserId = target.Id,
            SecurityVersion = target.SecurityVersion,
            IssuedAt = ToUnixSeconds(this.Clock.GetUtcNow()),
            Expiry = ToUnixSeconds(this.Clock.GetUtcNow() + TimeSpan.FromDays(7)),
        };

        var rebuilt = await this.Validator.ValidateAndRebuildAsync(session, ticket, default);

        Assert.That(rebuilt, Is.Not.Null);
        Assert.That(rebuilt!.Principal.FindFirst(AuthClaims.ImpersonatorIdClaim)?.Value, Is.EqualTo(admin.Id.ToString()));
    }

    [Test]
    public async Task CleanupInvalidAsync_RemovesOnlyInvalidRowsUpToLimit()
    {
        var user = await this.SeedUserAsync();
        var valid = this.MakeSession(user, securityVersion: 0);
        var expired = this.MakeSession(user, securityVersion: 0, expiresIn: TimeSpan.FromSeconds(-1));
        var versionMismatch = this.MakeSession(user, securityVersion: 99);
        this.Db.Sessions.AddRange(valid, expired, versionMismatch);
        await this.Db.SaveChangesAsync();

        var removed = await this.Validator.CleanupInvalidAsync(maximumRows: 1000, default);
        var remainingCount = await this.Db.Sessions.CountAsync();
        var validStillPresent = await this.Db.Sessions.AnyAsync(s => s.Token == valid.Token);

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(remainingCount, Is.EqualTo(1));
            Assert.That(validStillPresent, Is.True);
        });
    }

    [Test]
    public async Task CleanupInvalidAsync_RespectsMaximumRowsCap()
    {
        var user = await this.SeedUserAsync();
        for (var i = 0; i < 5; i++)
        {
            this.Db.Sessions.Add(this.MakeSession(user, securityVersion: 0, expiresIn: TimeSpan.FromSeconds(-1)));
        }

        await this.Db.SaveChangesAsync();

        var removed = await this.Validator.CleanupInvalidAsync(maximumRows: 2, default);
        var remainingCount = await this.Db.Sessions.CountAsync();

        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(remainingCount, Is.EqualTo(3));
        });
    }
}

public class SqliteTicketStoreTests
{
    private SqliteTestDatabase Sqlite = null!;
    private ServiceProvider Services = null!;
    private SqliteTicketStore Store = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(this.Sqlite.ConnectionString));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<SessionValidationService>();
        this.Services = services.BuildServiceProvider();
        this.Store = new SqliteTicketStore(this.Services);
    }

    [TearDown]
    public void TearDown()
    {
        this.Services.Dispose();
        this.Sqlite.Dispose();
    }

    private AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(this.Sqlite.ConnectionString)
        .Options);

    private async Task<User> SeedUserAsync(string email = "ticket@example.com")
    {
        await using var db = this.CreateContext();
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = "Ticket User",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Users.Add(user);
        db.Allowlist.Add(new AllowlistEntry { Id = Guid.NewGuid(), Email = email, CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return user;
    }

    private async Task<Session> SessionRow(string token)
    {
        await using var db = this.CreateContext();
        return (await db.Sessions.FindAsync(token))!;
    }

    [Test]
    public async Task StoreAsync_StampsOwnershipAndAFixedSevenDayExpiry()
    {
        var user = await this.SeedUserAsync();
        var ticket = new AuthenticationTicket(AuthClaims.BuildPrincipal(user), "Cookies");

        var token = await this.Store.StoreAsync(ticket);
        var row = await this.SessionRow(token);

        Assert.Multiple(() =>
        {
            Assert.That(row.UserId, Is.EqualTo(user.Id));
            Assert.That(row.SecurityVersion, Is.EqualTo(user.SecurityVersion));
            Assert.That(row.Expiry - row.IssuedAt, Is.EqualTo(TimeSpan.FromDays(7).TotalSeconds).Within(2));
        });
    }

    [Test]
    public async Task RenewAsync_NeverExtendsTheOriginalExpiryEvenWhenAskedTo()
    {
        var user = await this.SeedUserAsync();
        var ticket = new AuthenticationTicket(AuthClaims.BuildPrincipal(user), "Cookies");
        var token = await this.Store.StoreAsync(ticket);
        var original = await this.SessionRow(token);

        // The ticket asks for a much later expiry. It doesn't get one.
        var laterProperties = new AuthenticationProperties
        {
            IssuedUtc = DateTimeOffset.UtcNow.AddDays(3),
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30),
        };
        var renewalTicket = new AuthenticationTicket(AuthClaims.BuildPrincipal(user), laterProperties, "Cookies");
        await this.Store.RenewAsync(token, renewalTicket);

        var afterRenewal = await this.SessionRow(token);

        Assert.Multiple(() =>
        {
            Assert.That(afterRenewal.Expiry, Is.EqualTo(original.Expiry));
            Assert.That(afterRenewal.IssuedAt, Is.EqualTo(original.IssuedAt));
        });
    }

    [Test]
    public async Task RenewAsync_UpdatesOwnershipWhenTheTicketRepresentsADifferentIdentity()
    {
        var admin = await this.SeedUserAsync("admin@example.com");
        var target = await this.SeedUserAsync("target@example.com");
        var adminTicket = new AuthenticationTicket(AuthClaims.BuildPrincipal(admin), "Cookies");
        var token = await this.Store.StoreAsync(adminTicket);

        var impersonationTicket = new AuthenticationTicket(
            AuthClaims.BuildPrincipal(target, admin.Id, admin.Name), "Cookies");
        await this.Store.RenewAsync(token, impersonationTicket);

        var row = await this.SessionRow(token);

        Assert.Multiple(() =>
        {
            Assert.That(row.UserId, Is.EqualTo(target.Id));
            Assert.That(row.SecurityVersion, Is.EqualTo(target.SecurityVersion));
        });
    }
}
