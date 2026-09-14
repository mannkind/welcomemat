using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class ReservationsTests : TestBase
{
    [Test]
    public async Task Lists_seeded_reservation_and_ical_url()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        var checkIn = DateOnly.Parse(this.Seed.ReservationCheckIn);
        var row = this.Page.Locator(".reservations-timeline .reservation-row");
        await this.Expect(row).ToHaveCountAsync(1);
        await this.Expect(row).ToContainTextAsync(this.Seed.BeachName);
        await this.Expect(row).ToContainTextAsync(checkIn.ToString("MMM d"));

        await this.Expect(this.Page.Locator(".ical-subscription")).ToContainTextAsync($"/ical/{this.Seed.GuestCalToken}");
    }

    [Test]
    public async Task Change_dates_can_extend_a_reservation_from_the_calendar()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        var checkOut = DateOnly.Parse(this.Seed.ReservationCheckOut);
        var newCheckOut = checkOut.AddDays(1);

        await this.Page.GetByRole(AriaRole.Link, new() { Name = "Change Dates" }).ClickAsync();

        var bar = this.Page.Locator(".edit-bar");
        await this.Expect(bar).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator($".day[data-date='{checkOut.AddDays(-1):yyyy-MM-dd}']"))
            .ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bediting-stay\b"));

        await this.Page.Locator($".day[data-date='{this.Seed.ReservationCheckIn}']").ClickAsync();
        await this.Page.Locator($".day[data-date='{newCheckOut:yyyy-MM-dd}']").ClickAsync();

        var patch = await this.Page.RunAndWaitForResponseAsync(
            () => bar.GetByRole(AriaRole.Button, new() { Name = "Save Dates" }).ClickAsync(),
            r => r.Url.Contains("/reservations/") && r.Request.Method == "PATCH");
        Assert.That(patch.Status, Is.EqualTo(200));

        await this.GotoResilientAsync("/reservations");
        await this.Expect(this.Page.Locator(".reservation-meta").First)
            .ToContainTextAsync(newCheckOut.ToString("MMM d"));
    }

    // Shortening still works, and now it's the same gesture as lengthening.
    [Test]
    public async Task Change_dates_can_shorten_a_reservation()
    {
        await this.LoginAsGuest();

        var checkIn = DateOnly.Parse(this.Seed.ReservationCheckIn);
        var newCheckOut = DateOnly.Parse(this.Seed.ReservationCheckOut).AddDays(-1);

        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Link, new() { Name = "Change Dates" }).ClickAsync();

        await this.Page.Locator($".day[data-date='{checkIn:yyyy-MM-dd}']").ClickAsync();
        await this.Page.Locator($".day[data-date='{newCheckOut:yyyy-MM-dd}']").ClickAsync();

        var patch = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.Locator(".edit-bar").GetByRole(AriaRole.Button, new() { Name = "Save Dates" }).ClickAsync(),
            r => r.Url.Contains("/reservations/") && r.Request.Method == "PATCH");
        Assert.That(patch.Status, Is.EqualTo(200));

        await this.GotoResilientAsync("/reservations");
        await this.Expect(this.Page.Locator(".reservation-meta").First)
            .ToContainTextAsync(newCheckOut.ToString("MMM d"));
    }

    [Test]
    public async Task Change_dates_can_replace_both_dates()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        var checkIn = DateOnly.Parse(this.Seed.ReservationCheckIn);
        var checkOut = DateOnly.Parse(this.Seed.ReservationCheckOut);
        var newCheckIn = checkIn.AddDays(1);

        await this.Page.GetByRole(AriaRole.Link, new() { Name = "Change Dates" }).ClickAsync();
        await this.Page.Locator($".day[data-date='{newCheckIn:yyyy-MM-dd}']").ClickAsync();
        await this.Page.Locator($".day[data-date='{checkOut:yyyy-MM-dd}']").ClickAsync();

        var patch = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.Locator(".edit-bar").GetByRole(AriaRole.Button, new() { Name = "Save Dates" }).ClickAsync(),
            r => r.Url.Contains("/reservations/") && r.Request.Method == "PATCH");
        Assert.That(patch.Status, Is.EqualTo(200));

        await this.GotoResilientAsync("/reservations");
        await this.Expect(this.Page.Locator(".reservation-date").First)
            .ToContainTextAsync(newCheckIn.ToString("MMM"));
        await this.Expect(this.Page.Locator(".reservation-date").First)
            .ToContainTextAsync(newCheckIn.Day.ToString());
        await this.Expect(this.Page.Locator(".reservation-meta").First)
            .ToContainTextAsync(checkOut.ToString("MMM d"));
    }

    [Test]
    public async Task Normal_user_cannot_cancel_past_reservation()
    {
        await this.LoginAsGuest();

        var seedResp = await this.Page.APIRequest.PostAsync(
            $"{WebAppFixture.BaseUrl}/test-support/seed-past-reservation");
        Assert.That(seedResp.Ok, Is.True, "seeding the past reservation should succeed");
        var json = (await seedResp.JsonAsync())!.Value;
        var pastId = json.GetProperty("id").GetString()!;
        var pastCheckIn = DateOnly.Parse(json.GetProperty("checkIn").GetString()!);

        await this.Page.GotoAsync("/reservations");

        // Nothing offered in the UI...
        var pastRow = this.Page.Locator(".reservations-timeline.past .reservation-row",
            new() { HasText = pastCheckIn.ToString("MMM d") });
        await this.Expect(pastRow).ToHaveCountAsync(1);
        await this.Expect(pastRow.GetByRole(AriaRole.Button, new() { Name = "Cancel" })).ToHaveCountAsync(0);
        await this.Expect(pastRow.GetByRole(AriaRole.Link, new() { Name = "Change Dates" })).ToHaveCountAsync(0);

        var deleteResp = await this.Page.APIRequest.DeleteAsync(
            $"{WebAppFixture.BaseUrl}/reservations/{pastId}");
        Assert.That(deleteResp.Status, Is.EqualTo(400));
    }

    [Test]
    public async Task Past_reservations_are_ordered_most_recent_first()
    {
        await this.LoginAsGuest();

        var recentResp = await this.Page.APIRequest.PostAsync(
            $"{WebAppFixture.BaseUrl}/test-support/seed-past-reservation");
        Assert.That(recentResp.Ok, Is.True, "seeding recent past reservation should succeed");
        var recentCheckIn = DateOnly.Parse(
            (await recentResp.JsonAsync())!.Value.GetProperty("checkIn").GetString()!);

        var olderResp = await this.Page.APIRequest.PostAsync(
            $"{WebAppFixture.BaseUrl}/test-support/seed-older-past-reservation");
        Assert.That(olderResp.Ok, Is.True, "seeding older past reservation should succeed");
        var olderCheckIn = DateOnly.Parse(
            (await olderResp.JsonAsync())!.Value.GetProperty("checkIn").GetString()!);

        await this.Page.GotoAsync("/reservations");

        var rows = this.Page.Locator(".reservations-timeline.past .reservation-row");
        await this.Expect(rows).ToHaveCountAsync(2);

        await this.Expect(rows.First).ToContainTextAsync(recentCheckIn.ToString("MMM d"));
        await this.Expect(rows.Last).ToContainTextAsync(olderCheckIn.ToString("MMM d"));
    }

    [Test]
    public async Task Cancel_removes_the_reservation()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        var deleteTask = this.Page.WaitForResponseAsync(r =>
            r.Url.Contains("/reservations/") && r.Request.Method == "DELETE");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        var resp = await deleteTask;
        Assert.That(resp.Status, Is.EqualTo(200));

        await this.Page.WaitForURLAsync(url => url.Contains("/calendar"));

        await this.Page.GotoAsync("/reservations");
        await this.Expect(this.Page.GetByText("You have no upcoming reservations.")).ToBeVisibleAsync();
    }
}
