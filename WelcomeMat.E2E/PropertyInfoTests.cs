using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class PropertyInfoTests : TestBase
{
    private async Task SeedCurrentReservationAsync()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-current-reservation");
        Assert.That(resp.Ok, Is.True, "POST /test-support/seed-current-reservation should succeed");
    }

    [Test]
    public async Task Shows_property_name_and_wifi_reveal()
    {
        await this.SeedCurrentReservationAsync();
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = this.Seed.BeachName })).ToBeVisibleAsync();

        var wifi = this.Page.Locator("section").Filter(new() { HasText = "Network" });
        await this.Expect(wifi).ToContainTextAsync("BeachWifi");
        var password = wifi.GetByText("sunny123");
        await this.Expect(password).ToBeHiddenAsync();
        await wifi.GetByRole(AriaRole.Button, new() { Name = "Reveal" }).ClickAsync();
        await this.Expect(password).ToBeVisibleAsync();
    }

    [Test]
    public async Task Checkin_steps_are_visible_without_tab_click()
    {
        await this.SeedCurrentReservationAsync();
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        await this.Expect(this.Page.GetByText("Check-in Steps")).ToBeVisibleAsync();
        await this.Expect(this.Page.GetByText("Pick up keys from lockbox")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Map_renders_with_markers_and_spot_chip()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        // No tab click; the map is always on the page.
        await this.Expect(this.Page.Locator("#property-map.leaflet-container")).ToBeVisibleAsync(new() { Timeout = 15000 });
        Assert.That(await this.Page.Locator(".leaflet-marker-icon").CountAsync(), Is.GreaterThanOrEqualTo(1));
        await this.Expect(this.Page.GetByText("Best Tacos")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Guest_without_an_active_reservation_does_not_see_secrets()
    {
        // The seeded stay is far enough out that the window hasn't opened.
        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = this.Seed.BeachName })).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator(".quick-card-title", new() { HasTextString = "Door Code" })).ToHaveCountAsync(0);
        await this.Expect(this.Page.Locator("section").Filter(new() { HasText = "Network" })).ToHaveCountAsync(0);
        await this.Expect(this.Page.GetByText("BeachWifi")).ToHaveCountAsync(0);
        await this.Expect(this.Page.GetByText("4242")).ToHaveCountAsync(0);
        await this.Expect(this.Page.GetByText("will appear here starting 24 hours before")).ToBeVisibleAsync();

        await this.Expect(this.Page.GetByText("Check-in Steps")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_always_sees_secrets_even_without_a_reservation()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        await this.Expect(this.Page.Locator(".quick-card-title", new() { HasTextString = "Door Code" })).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator("section").Filter(new() { HasText = "Network" })).ToContainTextAsync("BeachWifi");
        await this.Expect(this.Page.GetByText("Check-in Steps")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Anonymous_image_request_is_blocked_and_never_returns_image_bytes()
    {
        // Deliberately no login call; this context is a stranger with a URL.
        var response = await this.Page.APIRequest.GetAsync(
            $"{WebAppFixture.BaseUrl}/images/{this.Seed.BeachId}/thumb.jpg",
            new() { MaxRedirects = 0 });

        Assert.That(response.Status, Is.EqualTo(302).Or.EqualTo(401));
        var contentType = response.Headers.TryGetValue("content-type", out var ct) ? ct : "";
        Assert.That(contentType, Does.Not.Contain("image/jpeg"));
    }

    [Test]
    public async Task Map_popups_render_admin_entered_text_safely_and_never_execute_it()
    {
        const string XssPayload = "<img src=x onerror=\"window.__welcomematXss=1\">";

        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#details");
        await this.Page.Locator("#property-name").FillAsync(XssPayload);
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");
        await this.Expect(this.Page.Locator("#property-map.leaflet-container")).ToBeVisibleAsync(new() { Timeout = 15000 });

        var markers = this.Page.Locator(".leaflet-marker-icon");
        var markerCount = await markers.CountAsync();
        Assert.That(markerCount, Is.GreaterThanOrEqualTo(1), "Expected at least the property marker.");

        for (var i = 0; i < markerCount; i++)
        {
            await markers.Nth(i).ClickAsync();
            var popup = this.Page.Locator(".leaflet-popup-content").Last;
            await this.Expect(popup).ToBeVisibleAsync();

            Assert.That(await popup.Locator("img").CountAsync(), Is.EqualTo(0),
                "A popup must never contain a real <img> element built from stored text.");

            // And nothing ran, which is the weaker claim but still worth having.
            var executed = await this.Page.EvaluateAsync<bool?>("() => window.__welcomematXss === 1");
            Assert.That(executed, Is.Not.True, "The stored payload must never execute.");

            await this.Page.Keyboard.PressAsync("Escape");
        }

        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = XssPayload })).ToBeVisibleAsync();
        var finalExecuted = await this.Page.EvaluateAsync<bool?>("() => window.__welcomematXss");
        Assert.That(finalExecuted, Is.Null.Or.EqualTo(false));
    }
}
