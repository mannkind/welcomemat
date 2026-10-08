using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Pages;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class CalendarModelTests
{
    private SqliteTestDatabase Sqlite = null!;
    private AppDbContext Db = null!;
    private User Guest = null!;
    private Property Property = null!;

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

        var now = DateTime.UtcNow;
        this.Guest = new User
        {
            Id = Guid.NewGuid(),
            Email = "guest@example.com",
            Name = "Dana Brooks",
            CreatedAt = now,
            UpdatedAt = now,
        };
        this.Db.Users.Add(this.Guest);

        this.Property = new Property
        {
            Id = Guid.NewGuid(),
            Name = "Test House",
            TimeZone = "America/Los_Angeles",
            CreatedAt = now,
            UpdatedAt = now,
        };
        this.Db.Properties.Add(this.Property);
        await this.Db.SaveChangesAsync();
    }

    [TearDown]
    public void TearDown()
    {
        this.Db.Dispose();
        this.Sqlite.Dispose();
    }

    private async Task AddStayAsync(DateOnly checkIn, DateOnly checkOut)
    {
        this.Db.Reservations.Add(new Reservation
        {
            Id = Guid.NewGuid(),
            UserId = this.Guest.Id,
            PropertyId = this.Property.Id,
            CheckIn = checkIn,
            CheckOut = checkOut,
            CreatedAt = DateTime.UtcNow,
        });
        await this.Db.SaveChangesAsync();
    }

    private async Task<CalendarModel> RenderAsync(int year, int month)
    {
        var clock = new PropertyClock(new ManualTimeProvider(new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero)));
        var model = new CalendarModel(this.Db, new ReservationService(new EfReservationRepository(this.Db), clock), clock)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, this.Guest.Id.ToString())], "test")),
                },
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
            },
        };

        await model.OnGetAsync(this.Property.Id, year, month);
        return model;
    }

    [Test]
    public async Task Stay_ending_on_the_first_of_the_month_marks_that_days_checkout()
    {
        // Nov 1 2026 is a Sunday; the grid starts on the checkout day itself.
        await this.AddStayAsync(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 1));

        var model = await this.RenderAsync(2026, 11);

        var novemberFirst = model.Days.Single(d => d.Date == new DateOnly(2026, 11, 1));
        Assert.That(novemberFirst.CheckoutReservation, Is.Not.Null);
        Assert.That(novemberFirst.Reservation, Is.Null, "the checkout day is bookable, not booked");
    }

    [Test]
    public async Task Stay_ending_the_day_before_the_grid_is_left_out()
    {
        await this.AddStayAsync(new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 31));

        var model = await this.RenderAsync(2026, 11);

        Assert.That(model.Days.Any(d => d.CheckoutReservation is not null || d.Reservation is not null), Is.False);
    }

    [Test]
    public async Task Stay_spanning_the_month_boundary_shows_on_both_months()
    {
        await this.AddStayAsync(new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 1));

        var october = await this.RenderAsync(2026, 10);
        var november = await this.RenderAsync(2026, 11);

        Assert.That(october.Days.Single(d => d.Date == new DateOnly(2026, 10, 30)).Reservation, Is.Not.Null);
        Assert.That(october.Days.Single(d => d.Date == new DateOnly(2026, 11, 1)).CheckoutReservation, Is.Not.Null);
        Assert.That(november.Days.Single(d => d.Date == new DateOnly(2026, 11, 1)).CheckoutReservation, Is.Not.Null);
    }
}
