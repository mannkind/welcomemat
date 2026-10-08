using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class GuestbookTests : TestBase
{
    private string GuestbookReservationId = "";

    [SetUp]
    public async Task SeedGuestbookReservationAsync()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-guestbook-reservation");
        Assert.That(resp.Ok, Is.True, "POST /test-support/seed-guestbook-reservation should succeed");
        var json = (await resp.JsonAsync())!.Value;
        this.GuestbookReservationId = json.GetProperty("id").GetString()!;
    }

    [Test]
    public async Task Guestbook_button_visible_for_in_window_reservation()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Guestbook_button_absent_for_future_reservation()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        var rows = this.Page.Locator(".reservation-row");
        var rowCount = await rows.CountAsync();
        Assert.That(rowCount, Is.EqualTo(2));

        var futureRow = this.Page.Locator(".reservations-timeline:not(.past) .reservation-row");
        await this.Expect(futureRow.GetByRole(AriaRole.Link, new() { Name = "Change Dates" })).ToBeVisibleAsync();
        await this.Expect(futureRow.GetByRole(AriaRole.Button, new() { Name = "Guestbook" })).ToBeHiddenAsync();
    }

    [Test]
    public async Task On_mobile_actions_stack_below_details_when_guestbook_present()
    {
        await this.Page.SetViewportSizeAsync(390, 844);
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");

        var row = this.Page.Locator(".reservations-timeline.past .reservation-row");
        await this.Expect(row.GetByRole(AriaRole.Button, new() { Name = "Guestbook" })).ToBeVisibleAsync();

        var details = await row.Locator(".reservation-details").BoundingBoxAsync();
        var actions = await row.Locator(".reservation-actions").BoundingBoxAsync();
        Assert.That(details, Is.Not.Null);
        Assert.That(actions, Is.Not.Null);

        Assert.That(actions!.Y, Is.GreaterThan(details!.Y + details.Height - 1),
            "On mobile the actions row should be below the details, not beside it.");

        // And nothing spills past the viewport edge.
        Assert.That(actions.X + actions.Width, Is.LessThanOrEqualTo(390),
            "Action buttons should stay within the mobile viewport width.");
    }

    [Test]
    public async Task Guest_can_write_a_guestbook_entry()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();

        var modal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(modal).ToBeVisibleAsync();

        await modal.Locator("#guestbook-message").FillAsync("What a wonderful stay!");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await this.Page.WaitForURLAsync(new System.Text.RegularExpressions.Regex("/reservations", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Guest_entry_appears_on_guestbook_view_page()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();
        var modal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(modal).ToBeVisibleAsync();
        await modal.Locator("#guestbook-message").FillAsync("Loved the beach views!");
        await this.Page.RunAndWaitForResponseAsync(
            () => modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/reservations"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);

        await this.GotoResilientAsync($"/guestbook?propertyId={this.Seed.BeachId}");
        var entries = this.Page.Locator(".guestbook-entries");
        await this.Expect(entries.GetByText("Loved the beach views!")).ToBeVisibleAsync();
        await this.Expect(entries.GetByText("Guest Tester")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Guest_can_edit_existing_guestbook_entry()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();
        var writeModal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(writeModal).ToBeVisibleAsync();
        await writeModal.Locator("#guestbook-message").FillAsync("Initial message.");
        // Same reload race as above.
        await this.Page.RunAndWaitForResponseAsync(
            () => writeModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/reservations"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" })).ToBeVisibleAsync();

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" }).ClickAsync();
        var modal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(modal).ToBeVisibleAsync();

        var textarea = modal.Locator("#guestbook-message");
        await this.Expect(textarea).ToHaveValueAsync("Initial message.");
        await textarea.FillAsync("Updated message.");
        await this.Page.RunAndWaitForResponseAsync(
            () => modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/reservations"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" })).ToBeVisibleAsync();

        await this.GotoResilientAsync($"/guestbook?propertyId={this.Seed.BeachId}");
        await this.Expect(this.Page.GetByText("Updated message.")).ToBeVisibleAsync();
        await this.Expect(this.Page.GetByText("Initial message.")).ToBeHiddenAsync();
    }

    [Test]
    public async Task Admin_can_view_guestbook_page()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/guestbook?propertyId={this.Seed.BeachId}");
        await this.Expect(this.Page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/guestbook"));
        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = $"Guestbook – {this.Seed.BeachName}" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Guestbook_tab_on_property_page_shows_entries()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();
        var modal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(modal).ToBeVisibleAsync();
        await modal.Locator("#guestbook-message").FillAsync("Great stay at the beach!");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // The button flipping to "Edit" is the signal the reload finished.
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" })).ToBeVisibleAsync();

        // The property page shows the guestbook inline, with no tab to click.
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        var entries = this.Page.Locator(".guestbook-entries");
        await this.Expect(entries.GetByText("Great stay at the beach!")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Admin_can_delete_guestbook_entry_from_property_edit()
    {
        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();
        var modal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(modal).ToBeVisibleAsync();
        await modal.Locator("#guestbook-message").FillAsync("Delete me please.");
        await modal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();
        // Same signal as before.
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Edit Guestbook" })).ToBeVisibleAsync();

        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#guestbook");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();

        var entries = this.Page.Locator(".guestbook-entries");
        await this.Expect(entries.GetByText("Delete me please.")).ToBeVisibleAsync();

        await entries.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await this.Expect(this.Page.GetByText("No guestbook entries yet.")).ToBeVisibleAsync();
    }
}
