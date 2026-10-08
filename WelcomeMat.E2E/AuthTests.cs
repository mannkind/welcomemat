using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AuthTests : TestBase
{
    [Test]
    public async Task Unauthenticated_protected_page_redirects_to_login()
    {
        await this.Page.GotoAsync("/reservations");
        await this.Expect(this.Page).ToHaveURLAsync(new Regex("/login", RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task Admin_sees_admin_nav_dropdown()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/reservations");
        await this.Expect(this.Page.Locator("summary.nav-dropdown-trigger")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_nav_dropdown_stays_on_screen_on_mobile_viewport()
    {
        await this.LoginAsAdmin();
        await this.Page.SetViewportSizeAsync(300, 844);
        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}");
        var trigger = this.Page.Locator("summary.nav-dropdown-trigger");
        await trigger.ClickAsync();

        var menu = this.Page.Locator(".nav-dropdown-content");
        await this.Expect(menu).ToBeVisibleAsync();
        var box = await menu.BoundingBoxAsync();
        var triggerBox = await trigger.BoundingBoxAsync();
        Assert.That(box, Is.Not.Null);
        Assert.That(triggerBox, Is.Not.Null);
        Assert.That(box!.X, Is.GreaterThanOrEqualTo(0), "dropdown should not overflow the left edge");
        Assert.That(box.X + box.Width, Is.LessThanOrEqualTo(300), "dropdown should not overflow the right edge");
        // Nudged back on-screen, but still visually attached to its trigger.
        Assert.That(box.Y - (triggerBox!.Y + triggerBox.Height), Is.InRange(0, 12),
            "dropdown should open directly under the trigger");
        Assert.That(box.X, Is.LessThanOrEqualTo(triggerBox.X),
            "dropdown should horizontally cover its trigger");
    }

    [Test]
    public async Task Guest_does_not_see_admin_nav_dropdown()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Expect(this.Page.GetByRole(AriaRole.Link, new() { Name = "My Reservations" })).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator("summary.nav-dropdown-trigger")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Logout_returns_to_login()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).ClickAsync();
        await this.Expect(this.Page).ToHaveURLAsync(new Regex("/login", RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task Login_preserves_return_url_to_property_edit()
    {
        var editUrl = $"/admin/properties/{this.Seed.BeachId}/edit";
        await this.Page.GotoAsync(editUrl);
        await this.Expect(this.Page).ToHaveURLAsync(new Regex(@"/login\?ReturnUrl=", RegexOptions.IgnoreCase));

        var link = this.Page.GetByRole(AriaRole.Link, new() { Name = "Sign in with Google" });
        var href = await link.GetAttributeAsync("href");
        Assert.That(href, Does.Contain("returnUrl="), "sign-in link should include returnUrl");
        Assert.That(href, Does.Contain(Uri.EscapeDataString(editUrl)), "sign-in link returnUrl should point at the edit page");

        // Standing in for the post-OAuth callback; the destination is what matters.
        await this.Page.GotoAsync($"/test-auth/login?email={Uri.EscapeDataString(this.Seed.AdminEmail)}&returnUrl={Uri.EscapeDataString(editUrl)}");
        await this.Expect(this.Page).ToHaveURLAsync(new Regex(Regex.Escape(editUrl), RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task Login_without_return_url_does_not_land_on_login()
    {
        await this.Page.GotoAsync($"/test-auth/login?email={Uri.EscapeDataString(this.Seed.AdminEmail)}");
        await this.Expect(this.Page).Not.ToHaveURLAsync(new Regex("/login", RegexOptions.IgnoreCase));
    }
}
