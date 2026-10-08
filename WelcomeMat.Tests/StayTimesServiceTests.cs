using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class StayTimesServiceTests
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
    public async Task GlobalTimes_ComeFromTheSeededSettingsRows()
    {
        var service = new StayTimesService(this.Db);

        var global = await service.GetGlobalAsync();

        Assert.Multiple(() =>
        {
            Assert.That(global.CheckIn, Is.EqualTo(new TimeOnly(15, 0)));
            Assert.That(global.CheckOut, Is.EqualTo(new TimeOnly(11, 0)));
        });
    }

    [Test]
    public async Task GlobalTimes_ReflectEditedSettingsRows()
    {
        await this.SetSetting("check_in_time", "16:30");
        await this.SetSetting("check_out_time", "10:00");
        var service = new StayTimesService(this.Db);

        var global = await service.GetGlobalAsync();

        Assert.Multiple(() =>
        {
            Assert.That(global.CheckIn, Is.EqualTo(new TimeOnly(16, 30)));
            Assert.That(global.CheckOut, Is.EqualTo(new TimeOnly(10, 0)));
        });
    }

    [Test]
    public async Task GlobalTimes_FallBackToDefaultsWhenUnparseable()
    {
        await this.SetSetting("check_in_time", "not a time");
        var service = new StayTimesService(this.Db);

        var global = await service.GetGlobalAsync();

        Assert.That(global.CheckIn, Is.EqualTo(StayTimesService.Defaults.CheckIn));
    }

    [Test]
    public async Task GlobalTimes_FallBackToDefaultsWhenRowsAreAbsent()
    {
        await this.DeleteSetting("check_in_time");
        await this.DeleteSetting("check_out_time");
        var service = new StayTimesService(this.Db);

        var global = await service.GetGlobalAsync();

        Assert.That(global, Is.EqualTo(StayTimesService.Defaults));
    }

    [Test]
    public void Resolve_NullOverridesInheritTheGlobalValue()
    {
        var global = new StayTimes { CheckIn = new TimeOnly(15, 0), CheckOut = new TimeOnly(11, 0) };

        var resolved = StayTimesService.Resolve(null, null, global);

        Assert.That(resolved, Is.EqualTo(global));
    }

    [Test]
    public void Resolve_PropertyOverridesWinIndependently()
    {
        var global = new StayTimes { CheckIn = new TimeOnly(15, 0), CheckOut = new TimeOnly(11, 0) };

        var resolved = StayTimesService.Resolve(new TimeOnly(17, 0), null, global);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.CheckIn, Is.EqualTo(new TimeOnly(17, 0)));
            Assert.That(resolved.CheckOut, Is.EqualTo(new TimeOnly(11, 0)));
        });
    }

    [TestCase(15, 0, 11, 0, true)]
    [TestCase(15, 0, 15, 0, true)]
    [TestCase(11, 0, 15, 0, false)]
    public void IsValidTurnover_RequiresCheckOutNoLaterThanCheckIn(
        int checkInHour, int checkInMinute, int checkOutHour, int checkOutMinute, bool valid)
    {
        var result = StayTimesService.IsValidTurnover(
            new TimeOnly(checkInHour, checkInMinute), new TimeOnly(checkOutHour, checkOutMinute));

        Assert.That(result, Is.EqualTo(valid));
    }

    [Test]
    public void IsValidTurnover_IsCheckedAgainstTheResolvedPair()
    {
        var global = new StayTimes { CheckIn = new TimeOnly(15, 0), CheckOut = new TimeOnly(11, 0) };

        var resolved = StayTimesService.Resolve(null, new TimeOnly(18, 0), global);

        Assert.That(StayTimesService.IsValidTurnover(resolved.CheckIn, resolved.CheckOut), Is.False);
    }

    [Test]
    public async Task PropertyOverrides_RoundTripThroughSqlite()
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        this.Db.Properties.Add(new Property
        {
            Id = id,
            Name = "Cabin",
            CheckInTime = new TimeOnly(17, 0),
            CheckOutTime = new TimeOnly(9, 30),
            CreatedAt = now,
            UpdatedAt = now,
        });
        await this.Db.SaveChangesAsync();
        this.Db.ChangeTracker.Clear();

        var reloaded = await this.Db.Properties.SingleAsync(p => p.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.CheckInTime, Is.EqualTo(new TimeOnly(17, 0)));
            Assert.That(reloaded.CheckOutTime, Is.EqualTo(new TimeOnly(9, 30)));
        });
    }

    [Test]
    public async Task PropertyOverrides_DefaultToNullMeaningInherit()
    {
        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();
        this.Db.Properties.Add(new Property { Id = id, Name = "Cabin", CreatedAt = now, UpdatedAt = now });
        await this.Db.SaveChangesAsync();
        this.Db.ChangeTracker.Clear();

        var reloaded = await this.Db.Properties.SingleAsync(p => p.Id == id);

        Assert.Multiple(() =>
        {
            Assert.That(reloaded.CheckInTime, Is.Null);
            Assert.That(reloaded.CheckOutTime, Is.Null);
        });
    }

    private async Task SetSetting(string key, string value)
    {
        var row = await this.Db.Settings.SingleOrDefaultAsync(s => s.Key == key);
        if (row is null)
        {
            this.Db.Settings.Add(new Setting { Key = key, Value = value });
        }
        else
        {
            row.Value = value;
        }

        await this.Db.SaveChangesAsync();
        this.Db.ChangeTracker.Clear();
    }

    private async Task DeleteSetting(string key)
    {
        var row = await this.Db.Settings.SingleOrDefaultAsync(s => s.Key == key);
        if (row is not null)
        {
            this.Db.Settings.Remove(row);
        }

        await this.Db.SaveChangesAsync();
        this.Db.ChangeTracker.Clear();
    }
}
