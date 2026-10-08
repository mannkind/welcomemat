using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AdminUsersTests : TestBase
{
    [SetUp]
    public async Task GoToUsers()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/users");
    }

    [Test]
    public async Task Can_add_an_allowlist_entry()
    {
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add User" }).ClickAsync();
        await this.Page.Locator("input[name=email]").FillAsync("newuser@test");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = "newuser@test" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Can_promote_a_user_to_admin()
    {
        var guestRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.GetByRole(AriaRole.Button, new() { Name = "Make Admin" }).ClickAsync();

        var updatedRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.GuestEmail });
        await this.Expect(updatedRow.GetByRole(AriaRole.Button, new() { Name = "Remove Admin" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_cannot_demote_self()
    {
        var adminRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.AdminEmail });
        await adminRow.GetByRole(AriaRole.Button, new() { Name = "Remove Admin" }).ClickAsync();

        await this.Page.GotoAsync("/admin/users");
        var reloadedRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.AdminEmail });
        await this.Expect(reloadedRow.GetByRole(AriaRole.Button, new() { Name = "Remove Admin" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Can_remove_a_user()
    {
        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email });
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Demoting_an_admin_in_another_browser_forces_a_login_redirect_on_their_next_request()
    {
        var guest2Row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email });
        await guest2Row.GetByRole(AriaRole.Button, new() { Name = "Make Admin" }).ClickAsync();
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email })
            .GetByRole(AriaRole.Button, new() { Name = "Remove Admin" })).ToBeVisibleAsync();

        await using var secondContext = await this.Browser!.NewContextAsync(new()
        {
            BaseURL = WebAppFixture.BaseUrl,
        });
        var secondPage = await secondContext.NewPageAsync();
        await secondPage.GotoAsync($"/test-auth/login?email={Uri.EscapeDataString(this.Seed.Guest2Email)}");
        await secondPage.GotoAsync("/admin/users");
        await this.Expect(secondPage).ToHaveURLAsync(new Regex("/admin/users"));

        var demoteRow = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email });
        await demoteRow.GetByRole(AriaRole.Button, new() { Name = "Remove Admin" }).ClickAsync();
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email })
            .GetByRole(AriaRole.Button, new() { Name = "Make Admin" })).ToBeVisibleAsync();

        await secondPage.GotoAsync("/admin/users");
        await this.Expect(secondPage).ToHaveURLAsync(new Regex("/login"));
    }

    [Test]
    public async Task Removing_an_admin_preserves_the_accounts_they_invited()
    {
        var guest2Row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email });
        await guest2Row.GetByRole(AriaRole.Button, new() { Name = "Make Admin" }).ClickAsync();
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email })
            .GetByRole(AriaRole.Button, new() { Name = "Remove Admin" })).ToBeVisibleAsync();

        await this.LoginAsync(this.Seed.Guest2Email);
        await this.Page.GotoAsync("/admin/users");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add User" }).ClickAsync();
        await this.Page.Locator("input[name=email]").FillAsync("invited-third-party@test");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = "invited-third-party@test" })).ToBeVisibleAsync();

        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/users");
        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var removeGuest2Row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email });
        await removeGuest2Row.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.Guest2Email })).ToHaveCountAsync(0);
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = "invited-third-party@test" })).ToBeVisibleAsync();
    }
}
