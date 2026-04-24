using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Tests;

public class BootstrapTests
{
    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;
    private BootstrapAdminService Bootstrap = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(this.Sqlite.ConnectionString)
            .Options);
        this.Bootstrap = new BootstrapAdminService(this.Db);
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    private async Task<User> SeedUserAsync(bool isAdmin, string email = "same@example.com")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            Name = email,
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        this.Db.Users.Add(user);
        await this.Db.SaveChangesAsync();
        return user;
    }

    private Task RunBootstrap(string? initialUser, bool requireInitialAdministrator = false) =>
        this.Bootstrap.BootstrapAsync(initialUser, requireInitialAdministrator, default);

    private Task<User?> UserByEmail(string email) =>
        this.Db.Users.FirstOrDefaultAsync(u => u.Email == email);

    [Test]
    public async Task EmptyInstallation_WithInitialUser_CreatesAdminAndAllowlistEntry()
    {
        await this.RunBootstrap("Admin@Example.com ");

        var user = await this.UserByEmail("admin@example.com");
        Assert.That(user, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(user!.IsAdmin, Is.True);
            Assert.That(this.Db.Allowlist.Any(a => a.Email == "admin@example.com"), Is.True);
        });
    }

    [Test]
    public async Task ExistingInstallation_DoesNotRepromoteInitialUser()
    {
        await this.SeedUserAsync(isAdmin: false);

        await this.RunBootstrap("same@example.com");

        Assert.That((await this.UserByEmail("same@example.com"))!.IsAdmin, Is.False);
    }

    [Test]
    public async Task PartialState_AllowlistOnlyWithNoUsers_DoesNotBootstrap()
    {
        this.Db.Allowlist.Add(new AllowlistEntry
        {
            Id = Guid.NewGuid(),
            Email = "orphan@example.com",
            CreatedAt = DateTime.UtcNow,
        });
        await this.Db.SaveChangesAsync();

        await this.RunBootstrap("same@example.com");

        Assert.That(await this.UserByEmail("same@example.com"), Is.Null);
    }

    [Test]
    public void EmptyProduction_WithoutInitialUser_ThrowsActionableError()
    {
        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => this.RunBootstrap(null, requireInitialAdministrator: true));

        Assert.That(ex!.Message, Does.Contain("INITIAL_USER"));
    }

    [Test]
    public async Task EmptyNonProduction_WithoutInitialUser_DoesNothingAndDoesNotThrow()
    {
        await this.RunBootstrap(null, requireInitialAdministrator: false);

        Assert.Multiple(() =>
        {
            Assert.That(this.Db.Users.Any(), Is.False);
            Assert.That(this.Db.Allowlist.Any(), Is.False);
        });
    }

    [Test]
    public async Task RemovedInitialUser_IsNeverRecreated()
    {
        await this.RunBootstrap("same@example.com");
        this.Db.Users.RemoveRange(this.Db.Users);
        this.Db.Allowlist.RemoveRange(this.Db.Allowlist);
        await this.Db.SaveChangesAsync();

        await this.RunBootstrap("same@example.com");

        Assert.That(await this.UserByEmail("same@example.com"), Is.Null);
    }

    [Test]
    public async Task PreExistingInstallationPredatingTheMarker_IsNeverBootstrappedEvenAfterDataIsRemoved()
    {
        await this.SeedUserAsync(isAdmin: false, email: "existing@example.com");
        await this.RunBootstrap(null, requireInitialAdministrator: false);

        this.Db.Users.RemoveRange(this.Db.Users);
        await this.Db.SaveChangesAsync();

        await this.RunBootstrap("existing@example.com");

        Assert.That(await this.UserByEmail("existing@example.com"), Is.Null);
    }

    [Test]
    public async Task NormalizesEmailCaseAndWhitespace()
    {
        await this.RunBootstrap("  MixedCase@Example.COM\t");

        Assert.That(await this.UserByEmail("mixedcase@example.com"), Is.Not.Null);
    }
}
