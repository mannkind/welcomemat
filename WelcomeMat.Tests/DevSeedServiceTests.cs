using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;

namespace WelcomeMat.Tests;

public class DevSeedServiceTests
{
    private SqliteTestDatabase Sqlite = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
    }

    [TearDown]
    public void TearDown() => this.Sqlite.Dispose();

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(this.Sqlite.ConnectionString)
        .Options);

    [Test]
    public async Task WipeAsync_restores_settings_changed_by_a_prior_test_to_migration_defaults()
    {
        await using (var db = this.NewDb())
        {
            var siteTitle = await db.Settings.SingleAsync(s => s.Key == "site_title");
            siteTitle.Value = "My Cabins";
            var maxNights = await db.Settings.SingleAsync(s => s.Key == "max_consecutive_nights");
            maxNights.Value = "5";
            await db.SaveChangesAsync();
        }

        await using (var db = this.NewDb())
        {
            await DevSeedService.WipeAsync(db);
        }

        await using (var db = this.NewDb())
        {
            var settings = await db.Settings.ToDictionaryAsync(s => s.Key, s => s.Value);
            Assert.That(settings["site_title"], Is.EqualTo("WelcomeMat"));
            Assert.That(settings["max_consecutive_nights"], Is.EqualTo("7"));
            Assert.That(settings["min_advance_notice_days"], Is.EqualTo("1"));
            Assert.That(settings["max_advance_booking_days"], Is.EqualTo("90"));
            Assert.That(settings, Contains.Key("admin_ical_token"));
        }
    }

    [Test]
    public async Task WipeAsync_does_not_delete_settings_rows_outright()
    {
        await using var before = this.NewDb();
        var countBefore = await before.Settings.CountAsync();

        await DevSeedService.WipeAsync(before);

        await using var after = this.NewDb();
        var countAfter = await after.Settings.CountAsync();
        Assert.That(countAfter, Is.EqualTo(countBefore));
    }
}
