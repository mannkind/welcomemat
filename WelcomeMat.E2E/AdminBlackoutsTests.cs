using System;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AdminBlackoutsTests : TestBase
{
    [SetUp]
    public async Task GoToBlackouts()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/blackouts");
    }

    [Test]
    public async Task Can_add_a_global_blackout()
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(40).ToString("yyyy-MM-dd");
        await this.AddBlackoutAsync(date, date, "Holiday", propertyLabel: null);

        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = date });
        await this.Expect(row).ToContainTextAsync("Global");
        await this.Expect(row).ToContainTextAsync("Holiday");
    }

    [Test]
    public async Task Can_add_a_property_specific_blackout()
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(45).ToString("yyyy-MM-dd");
        await this.AddBlackoutAsync(date, date, "Maintenance", propertyLabel: this.Seed.BeachName);

        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = date });
        await this.Expect(row).ToContainTextAsync(this.Seed.BeachName);
    }

    [Test]
    public async Task Blackout_appears_on_the_calendar()
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(50);
        var dateStr = date.ToString("yyyy-MM-dd");
        await this.AddBlackoutAsync(dateStr, dateStr, "Closed", propertyLabel: null);

        await this.Page.GotoAsync($"/calendar/{this.Seed.BeachId}?year={date.Year}&month={date.Month}");
        await this.Expect(this.Page.Locator($".day.blackout[data-date='{dateStr}']")).ToHaveCountAsync(1);
    }

    [Test]
    public async Task Can_remove_a_blackout()
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(55).ToString("yyyy-MM-dd");
        await this.AddBlackoutAsync(date, date, "Temp", propertyLabel: null);

        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = date });
        await row.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();

        await this.Expect(this.Page.Locator("tr").Filter(new() { HasText = date })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_367_day_blackout_range_is_rejected()
    {
        var start = DateOnly.FromDateTime(DateTime.Today).AddDays(100);
        var end = start.AddDays(366); // 367-day inclusive span, one past the limit.

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Blackout" }).ClickAsync();
        await this.Page.Locator("#start_date").FillAsync(start.ToString("yyyy-MM-dd"));
        await this.Page.Locator("#end_date").FillAsync(end.ToString("yyyy-MM-dd"));
        await this.Page.Locator("input[name=reason]").FillAsync("Too long");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("366 days");
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = start.ToString("yyyy-MM-dd") })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task A_366_day_blackout_range_is_accepted()
    {
        var start = DateOnly.FromDateTime(DateTime.Today).AddDays(100);
        var end = start.AddDays(365); // exactly 366 inclusive days: the maximum allowed span.

        await this.AddBlackoutAsync(start.ToString("yyyy-MM-dd"), end.ToString("yyyy-MM-dd"), "Max span", propertyLabel: null);

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = start.ToString("yyyy-MM-dd") })).ToBeVisibleAsync();
    }

    [Test]
    public async Task An_overlong_blackout_reason_is_rejected()
    {
        var date = DateOnly.FromDateTime(DateTime.Today).AddDays(60).ToString("yyyy-MM-dd");
        var overlongReason = new string('x', 4001);

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Blackout" }).ClickAsync();
        await this.Page.Locator("#start_date").FillAsync(date);
        await this.Page.Locator("#end_date").FillAsync(date);
        await this.Page.Locator("input[name=reason]").FillAsync(overlongReason);
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("too long");
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = date })).ToHaveCountAsync(0);
    }

    private async Task AddBlackoutAsync(string start, string end, string reason, string? propertyLabel)
    {
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Blackout" }).ClickAsync();
        await this.Page.Locator("#start_date").FillAsync(start);
        await this.Page.Locator("#end_date").FillAsync(end);
        await this.Page.Locator("input[name=reason]").FillAsync(reason);
        if (propertyLabel is not null)
        {
            await this.Page.Locator("select[name=propertyId]").SelectOptionAsync(new SelectOptionValue { Label = propertyLabel });
        }

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = start })).ToBeVisibleAsync();
    }
}
