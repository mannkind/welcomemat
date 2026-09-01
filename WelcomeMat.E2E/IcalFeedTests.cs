using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class IcalFeedTests : TestBase
{

    [Test]
    public async Task Property_feed_includes_guest_name()
    {
        var resp = await this.Page.APIRequest.GetAsync($"/ical/{this.Seed.BeachIcalToken}");
        Assert.That(resp.Ok, Is.True);

        var body = await resp.TextAsync();
        Assert.That(body, Does.Contain(this.Seed.BeachName));
        Assert.That(body, Does.Contain("Guest Tester"));
    }



    [Test]
    public async Task Unknown_token_returns_404()
    {
        var resp = await this.Page.APIRequest.GetAsync("/ical/not-a-real-token");
        Assert.That(resp.Status, Is.EqualTo(404));
    }


    [Test]
    public async Task Admin_can_rotate_property_calendar_token_and_old_url_stops_working()
    {
        var oldToken = this.Seed.BeachIcalToken;
        var oldResp = await this.Page.APIRequest.GetAsync($"/ical/{oldToken}");
        Assert.That(oldResp.Ok, Is.True, "old token should work before rotation");

        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/properties");

        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.BeachName });
        await this.Page.RunAndWaitForResponseAsync(
            () => row.GetByRole(AriaRole.Button, new() { Name = "Rotate iCal" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/admin/properties"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);

        await this.GotoResilientAsync($"/admin/properties/{this.Seed.BeachId}/edit#details");
        var href = await this.Page.Locator(".ical-subscription a").GetAttributeAsync("href");
        Assert.That(href, Is.Not.Null.And.Not.Empty);
        var newToken = href!.Split('/')[^1];
        Assert.That(newToken, Is.Not.EqualTo(oldToken));

        var oldAfterResp = await this.Page.APIRequest.GetAsync($"/ical/{oldToken}");
        Assert.That(oldAfterResp.Status, Is.EqualTo(404), "old token should 404 after rotation");

        var newResp = await this.Page.APIRequest.GetAsync($"/ical/{newToken}");
        Assert.That(newResp.Ok, Is.True, "new token should work after rotation");
    }

    [Test]
    public async Task Admin_feed_includes_every_active_reservation_and_token_rotates()
    {
        var seedResp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-past-reservation");
        Assert.That(seedResp.Ok, Is.True);
        var secondReservationId = (await seedResp.JsonAsync())!.Value.GetProperty("id").GetString();

        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/settings");

        var href = await this.Page.Locator(".ical-subscription a").GetAttributeAsync("href");
        Assert.That(href, Is.Not.Null.And.Not.Empty);
        var oldToken = href!.Split('/')[^1];

        var feedResp = await this.Page.APIRequest.GetAsync($"/ical/{oldToken}");
        Assert.That(feedResp.Ok, Is.True);
        var body = await feedResp.TextAsync();
        Assert.That(body, Does.Contain($"reservation-{this.Seed.ReservationId}"));
        Assert.That(body, Does.Contain($"reservation-{secondReservationId}"));
        Assert.That(body, Does.Contain("Guest Tester"));

        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "Rotate Link" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/admin/settings"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);

        var newHref = await this.Page.Locator(".ical-subscription a").GetAttributeAsync("href");
        Assert.That(newHref, Is.Not.Null.And.Not.Empty);
        var newToken = newHref!.Split('/')[^1];
        Assert.That(newToken, Is.Not.EqualTo(oldToken));

        var oldAfterResp = await this.Page.APIRequest.GetAsync($"/ical/{oldToken}");
        Assert.That(oldAfterResp.Status, Is.EqualTo(404), "old admin token should 404 after rotation");

        var newResp = await this.Page.APIRequest.GetAsync($"/ical/{newToken}");
        Assert.That(newResp.Ok, Is.True, "new admin token should work after rotation");
    }
}
