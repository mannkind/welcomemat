using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class ImpersonationTests : TestBase
{
    [SetUp]
    public async Task GoToUsers()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/users");
    }

    [Test]
    public async Task Admin_can_impersonate_a_guest()
    {
        var guestRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.GetByRole(AriaRole.Button, new() { Name = "Impersonate" }).ClickAsync();

        await this.Expect(this.Page).Not.ToHaveURLAsync(new Regex("/admin", RegexOptions.IgnoreCase));
        await this.Expect(this.Page.Locator(".impersonation-banner")).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator("summary.nav-dropdown-trigger")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Impersonated_session_cannot_reach_admin_pages()
    {
        var guestRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.GetByRole(AriaRole.Button, new() { Name = "Impersonate" }).ClickAsync();

        await this.Expect(this.Page.Locator(".impersonation-banner")).ToBeVisibleAsync();

        await this.Page.GotoAsync("/admin/users");
        await this.Expect(this.Page).ToHaveURLAsync(new Regex("AccessDenied", RegexOptions.IgnoreCase));
    }

    [Test]
    public async Task Return_to_admin_restores_the_admin_session()
    {
        var guestRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.GetByRole(AriaRole.Button, new() { Name = "Impersonate" }).ClickAsync();
        await this.Expect(this.Page.Locator(".impersonation-banner")).ToBeVisibleAsync();

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Return to admin" }).ClickAsync();

        await this.Expect(this.Page.Locator(".impersonation-banner")).ToHaveCountAsync(0);
        await this.Page.GotoAsync("/admin/users");
        await this.Expect(this.Page.Locator("summary.nav-dropdown-trigger")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_cannot_impersonate_self_or_other_admins()
    {
        // Not on their own row...
        var adminRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.AdminEmail });
        await this.Expect(adminRow.GetByRole(AriaRole.Button, new() { Name = "Impersonate" })).ToHaveCountAsync(0);

        var guestRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await this.Expect(guestRow.GetByRole(AriaRole.Button, new() { Name = "Impersonate" })).ToBeVisibleAsync();
    }
}
