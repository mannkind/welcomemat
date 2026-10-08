using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AdminSettingsTests : TestBase
{
    [SetUp]
    public async Task GoToSettings()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/settings");
    }

    [Test]
    public async Task Saving_site_title_updates_nav_brand()
    {
        await this.Page.Locator("#site_title").FillAsync("My Cabins");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await this.Expect(this.Page.GetByText("Settings saved.")).ToBeVisibleAsync();

        await this.Page.GotoAsync("/admin/settings");
        await this.Expect(this.Page.Locator("#site_title")).ToHaveValueAsync("My Cabins");
        await this.Expect(this.Page.Locator(".nav-brand")).ToContainTextAsync("My Cabins");
    }

    [Test]
    public async Task Saving_stay_rules_persists()
    {
        await this.Page.Locator("#max_consecutive_nights").FillAsync("5");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        await this.Expect(this.Page.GetByText("Settings saved.")).ToBeVisibleAsync();

        await this.Page.GotoAsync("/admin/settings");
        await this.Expect(this.Page.Locator("#max_consecutive_nights")).ToHaveValueAsync("5");
    }

    [Test]
    public async Task Negative_max_consecutive_nights_is_rejected()
    {
        await this.Page.Locator("#max_consecutive_nights").FillAsync("0");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("consecutive nights");

        await this.Page.GotoAsync("/admin/settings");
        await this.Expect(this.Page.Locator("#max_consecutive_nights")).Not.ToHaveValueAsync("0");
    }

    [Test]
    public async Task Negative_min_advance_notice_is_rejected()
    {
        await this.Page.Locator("#min_advance_notice_days").FillAsync("-1");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("advance notice");

        await this.Page.GotoAsync("/admin/settings");
        await this.Expect(this.Page.Locator("#min_advance_notice_days")).Not.ToHaveValueAsync("-1");
    }

    [Test]
    public async Task Max_advance_booking_days_over_3650_is_rejected()
    {
        await this.Page.Locator("#max_advance_booking_days").FillAsync("3651");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("advance booking");

        await this.Page.GotoAsync("/admin/settings");
        await this.Expect(this.Page.Locator("#max_advance_booking_days")).Not.ToHaveValueAsync("3651");
    }

    [Test]
    public async Task Overlong_site_title_is_rejected()
    {
        await this.Page.Locator("#site_title").FillAsync(new string('x', 201));
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("too long");
    }
}
