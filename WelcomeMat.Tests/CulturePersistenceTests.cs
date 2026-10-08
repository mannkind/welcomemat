using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;

namespace WelcomeMat.Tests;

public class CulturePersistenceTests
{
    private static readonly CultureInfo[] Cultures =
    [
        CultureInfo.InvariantCulture,
        new CultureInfo("de-DE"),
        new CultureInfo("ar-SA"),
    ];

    private SqliteTestDatabase Sqlite = null!;
    private CultureInfo OriginalCulture = null!;

    [SetUp]
    public async Task SetUp()
    {
        this.Sqlite = new SqliteTestDatabase();
        await this.Sqlite.RunnerWithAllMigrations().RunAsync();
        this.OriginalCulture = CultureInfo.CurrentCulture;
    }

    [TearDown]
    public void TearDown()
    {
        CultureInfo.CurrentCulture = this.OriginalCulture;
        this.Sqlite.Dispose();
    }

    private AppDbContext NewDb() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlite(this.Sqlite.ConnectionString)
        .Options);

    [TestCaseSource(nameof(Cultures))]
    public async Task DateOnly_round_trips_exactly_under_every_culture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;

        var checkIn = new DateOnly(2026, 7, 26);
        var checkOut = new DateOnly(2026, 8, 3);
        var userId = Guid.NewGuid();
        var propertyId = Guid.NewGuid();
        var reservationId = Guid.NewGuid();
        var now = new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc);

        await using (var db = this.NewDb())
        {
            db.Users.Add(new User { Id = userId, Email = "culture@example.com", Name = "Culture Tester", CreatedAt = now, UpdatedAt = now });
            db.Properties.Add(new Property { Id = propertyId, Name = "Culture Property", CreatedAt = now, UpdatedAt = now });
            db.Reservations.Add(new Reservation
            {
                Id = reservationId,
                UserId = userId,
                PropertyId = propertyId,
                CheckIn = checkIn,
                CheckOut = checkOut,
                Notes = "",
                CreatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = this.NewDb())
        {
            var reservation = await db.Reservations.SingleAsync(r => r.Id == reservationId);
            Assert.That(reservation.CheckIn, Is.EqualTo(checkIn));
            Assert.That(reservation.CheckOut, Is.EqualTo(checkOut));
            Assert.That(reservation.CreatedAt, Is.EqualTo(now));
        }

        Assert.That(await this.RawColumnValue("reservations", "check_in", reservationId), Is.EqualTo("2026-07-26"));
        Assert.That(await this.RawColumnValue("reservations", "check_out", reservationId), Is.EqualTo("2026-08-03"));
    }

    [TestCaseSource(nameof(Cultures))]
    public async Task DateTime_round_trips_exactly_under_every_culture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;

        var propertyId = Guid.NewGuid();
        var createdAt = new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc);

        await using (var db = this.NewDb())
        {
            db.Properties.Add(new Property { Id = propertyId, Name = "Culture Property", CreatedAt = createdAt, UpdatedAt = createdAt });
            await db.SaveChangesAsync();
        }

        await using (var db = this.NewDb())
        {
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.That(property.CreatedAt, Is.EqualTo(createdAt));
        }

        Assert.That(await this.RawColumnValue("properties", "created_at", propertyId), Is.EqualTo("2026-12-31 23:59:59"));
    }

    [TestCaseSource(nameof(Cultures))]
    public async Task Coordinates_round_trip_exactly_under_every_culture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;

        var propertyId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        const double lat = 34.019537;
        const double lon = -118.491199;

        await using (var db = this.NewDb())
        {
            db.Properties.Add(new Property
            {
                Id = propertyId,
                Name = "Culture Property",
                Latitude = lat,
                Longitude = lon,
                CreatedAt = now,
                UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }

        await using (var db = this.NewDb())
        {
            var property = await db.Properties.SingleAsync(p => p.Id == propertyId);
            Assert.That(property.Latitude, Is.EqualTo(lat));
            Assert.That(property.Longitude, Is.EqualTo(lon));
        }
    }

    private async Task<string?> RawColumnValue(string table, string column, Guid id)
    {
        await using var conn = new Microsoft.Data.Sqlite.SqliteConnection(this.Sqlite.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {column} FROM {table} WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        var result = await cmd.ExecuteScalarAsync();
        return result as string;
    }
}
