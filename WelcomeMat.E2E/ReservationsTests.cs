using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class ReservationsTests : TestBase
{
    [Test]
    public async Task Lists_seeded_reservation()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        var checkIn = DateOnly.Parse(this.Seed.ReservationCheckIn);
        var row = this.Page.Locator(".reservations-timeline .reservation-row");
        await this.Expect(row).ToHaveCountAsync(1);
        await this.Expect(row).ToContainTextAsync(this.Seed.BeachName);
        await this.Expect(row).ToContainTextAsync(checkIn.ToString("MMM d"));

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

}
