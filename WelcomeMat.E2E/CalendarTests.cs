using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class CalendarTests : TestBase
{
    [Test]
    public async Task Legend_and_hint_are_visible()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}");

        await this.Expect(this.Page.Locator(".calendar-legend")).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator(".calendar-legend .legend-item")).ToHaveCountAsync(5);
        await this.Expect(this.Page.GetByText("Click an available date")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Month_navigation_changes_displayed_month()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}");

        var heading = this.Page.Locator(".calendar-header h2");
        var before = await heading.InnerTextAsync();
        await this.Page.Locator(".calendar-header a.btn").Last.ClickAsync(); // next month (→)
        await this.Expect(heading).Not.ToHaveTextAsync(before);
    }

    [Test]
    public async Task Guest_can_create_a_reservation()
    {
        await this.LoginAsGuest();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var nextMonth = today.AddDays(1).AddMonths(1);
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}?year={nextMonth.Year}&month={nextMonth.Month}");

        var available = (await this.Page.Locator(".day.available[data-date]")
                .EvaluateAllAsync<string[]>("els => els.map(e => e.getAttribute('data-date'))"))
            .Select(DateOnly.Parse)
            .Where(d => d > today)
            .OrderBy(d => d)
            .ToList();

        var seededCheckIn = DateOnly.Parse(this.Seed.ReservationCheckIn);
        var seededCheckOut = DateOnly.Parse(this.Seed.ReservationCheckOut);

        var (start, end) = FindBookablePair(available, seededCheckIn, seededCheckOut);

        await this.Page.Locator($".day[data-date='{start:yyyy-MM-dd}']").ClickAsync();
        await this.Page.Locator($".day[data-date='{end:yyyy-MM-dd}']").ClickAsync();

        var bookButton = this.Page.GetByRole(AriaRole.Button, new() { Name = "Book" });
        await this.Expect(bookButton).ToBeVisibleAsync();

        var postTask = this.Page.WaitForResponseAsync(r =>
            r.Url.EndsWith("/reservations") && r.Request.Method == "POST");
        var navTask = this.Page.WaitForResponseAsync(r =>
            r.Request.IsNavigationRequest && r.Url.Contains("/calendar"));
        await bookButton.ClickAsync();
        var resp = await postTask;
        Assert.That(resp.Status, Is.EqualTo(200));
        await navTask;
        await this.Page.WaitForLoadStateAsync(LoadState.Load);

        // Two now: the seeded one plus what this test just booked.
        await this.GotoResilientAsync("/reservations");
        await this.Expect(this.Page.Locator(".reservation-row")).ToHaveCountAsync(2);
    }

    [Test]
    public async Task Checkout_day_shows_the_departing_guests_initials()
    {
        await this.LoginAsGuest();

        var checkOut = DateOnly.Parse(this.Seed.ReservationCheckOut);
        var lastNight = checkOut.AddDays(-1);
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}?year={checkOut.Year}&month={checkOut.Month}");

        var staying = this.Page.Locator($".day[data-date='{lastNight:yyyy-MM-dd}'] .day-initials:not(.day-initials-out)");
        var leaving = this.Page.Locator($".day[data-date='{checkOut:yyyy-MM-dd}'] .day-initials-out");

        await this.Expect(leaving).ToBeVisibleAsync();
        await this.Expect(leaving).ToHaveTextAsync(await staying.InnerTextAsync());
        await this.Expect(leaving).ToHaveAttributeAsync("title", new System.Text.RegularExpressions.Regex("checks out$"));
    }

    [Test]
    public async Task Turnover_day_shows_both_the_departing_and_arriving_guests()
    {
        await this.LoginAsAdmin();

        var checkOut = DateOnly.Parse(this.Seed.ReservationCheckOut);
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}?year={checkOut.Year}&month={checkOut.Month}");

        await this.Page.Locator($".day[data-date='{checkOut:yyyy-MM-dd}']").ClickAsync();
        await this.Page.Locator($".day[data-date='{checkOut.AddDays(1):yyyy-MM-dd}']").ClickAsync();
        var booked = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "Book" }).ClickAsync(),
            r => r.Url.EndsWith("/reservations") && r.Request.Method == "POST");
        Assert.That(booked.Status, Is.EqualTo(200));

        await this.GotoResilientAsync($"/calendar/{this.Seed.BeachId}?year={checkOut.Year}&month={checkOut.Month}");

        var cell = this.Page.Locator($".day[data-date='{checkOut:yyyy-MM-dd}']");
        var leaving = cell.Locator(".day-initials-out");
        var arriving = cell.Locator(".day-initials:not(.day-initials-out)");

        await this.Expect(cell).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bbooked\b"));
        await this.Expect(leaving).ToBeVisibleAsync();
        await this.Expect(arriving).ToBeVisibleAsync();
        Assert.That(await leaving.InnerTextAsync(), Is.Not.EqualTo(await arriving.InnerTextAsync()));
    }

    private static (DateOnly Start, DateOnly End) FindBookablePair(
        List<DateOnly> available, DateOnly seededCheckIn, DateOnly seededCheckOut)
    {
        var set = available.ToHashSet();
        foreach (var d in available)
        {
            var next = d.AddDays(1);
            var bookedNightClear = d < seededCheckIn || d >= seededCheckOut;
            if (set.Contains(next) && bookedNightClear)
            {
                return (d, next);
            }
        }
        throw new InvalidOperationException("No bookable consecutive day pair found in the month view.");
    }
}
