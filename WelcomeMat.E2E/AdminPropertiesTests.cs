using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AdminPropertiesTests : TestBase
{
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRof" +
        "Hh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAAB" +
        "AAAAAAAAAAAAAAAAAAAAAv/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AfwD/2Q==");

    [Test]
    public async Task Quick_add_redirects_to_edit_page()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/properties");

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "New Property" }).ClickAsync();
        await this.Page.Locator("input[name=name]").FillAsync("Test Cottage");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Property" }).ClickAsync();

        await this.Expect(this.Page).ToHaveURLAsync(new Regex(@"/admin/properties/[0-9a-fA-F-]+/edit"));
        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = "Edit Property: Test Cottage" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Details_save_persists()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#details");

        await this.Page.Locator("#property-wifi-network").FillAsync("NewWifiNet");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();

        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#details");
        await this.Expect(this.Page.Locator("#property-wifi-network")).ToHaveValueAsync("NewWifiNet");
    }

    [Test]
    public async Task Photo_upload_and_delete()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        await this.Expect(this.Page.Locator(".gallery-img")).ToHaveCountAsync(1);

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();
        await this.Expect(this.Page.GetByText("No photos uploaded yet.")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Uploaded_photo_opens_in_lightbox_on_property_page()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        // The lightbox lives on the public property page, not the admin one.
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");
        await this.Page.Locator(".gallery-img").First.ClickAsync();
        await this.Expect(this.Page.Locator("#lightbox-modal")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Lightbox_keeps_image_and_close_button_within_viewport()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");
        await this.Page.Locator(".gallery-img").First.ClickAsync();

        var img = this.Page.Locator("#lightbox-img");
        await this.Expect(img).ToBeVisibleAsync();

        var viewport = this.Page.ViewportSize!;
        var imgBox = (await img.BoundingBoxAsync())!;
        var closeBox = (await this.Page.Locator("#lightbox-close").BoundingBoxAsync())!;

        Assert.Multiple(() =>
        {
            Assert.That(imgBox.X, Is.GreaterThanOrEqualTo(0), "image left edge off-screen");
            Assert.That(imgBox.Y, Is.GreaterThanOrEqualTo(0), "image top edge off-screen");
            Assert.That(imgBox.X + imgBox.Width, Is.LessThanOrEqualTo(viewport.Width), "image right edge off-screen");
            Assert.That(imgBox.Y + imgBox.Height, Is.LessThanOrEqualTo(viewport.Height), "image bottom edge off-screen");
        });

        Assert.Multiple(() =>
        {
            Assert.That(closeBox.X + closeBox.Width, Is.GreaterThan(viewport.Width - 100), "close button not near right edge");
            Assert.That(closeBox.X + closeBox.Width, Is.LessThanOrEqualTo(viewport.Width), "close button past right edge");
            Assert.That(closeBox.Y, Is.LessThan(100), "close button not near top edge");
        });
    }

    [Test]
    public async Task Set_thumbnail_shows_on_property_list()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Set as thumbnail" }).ClickAsync();
        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = "Current Thumbnail" })).ToBeVisibleAsync();

        await this.Page.GotoAsync("/property-list");
        var card = this.Page.Locator(".property-card").Filter(new() { HasText = this.Seed.BeachName });
        await this.Expect(card.Locator("img")).ToHaveAttributeAsync("src", $"/images/{this.Seed.BeachId}/thumb.jpg");
        Assert.That(await card.Locator("img").EvaluateAsync<int>("img => img.naturalWidth"), Is.GreaterThan(0));
    }

    [Test]
    public async Task Remove_thumbnail_reverts_to_placeholder()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Set as thumbnail" }).ClickAsync();

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Remove Thumbnail" }).ClickAsync();
        await this.Expect(this.Page.GetByRole(AriaRole.Heading, new() { Name = "Current Thumbnail" })).ToHaveCountAsync(0);

        await this.Page.GotoAsync("/property-list");
        var card = this.Page.Locator(".property-card").Filter(new() { HasText = this.Seed.BeachName });
        await this.Expect(card.Locator("img")).ToHaveAttributeAsync("src", "/images/placeholder.jpg");
    }

    private async Task UploadPhotoAsync()
    {
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#photos");
        await this.Page.Locator("#photo-upload").SetInputFilesAsync(new FilePayload
        {
            Name = "test.jpg",
            MimeType = "image/jpeg",
            Buffer = TinyJpeg,
        });
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Upload", Exact = true }).ClickAsync();
    }

    [Test]
    public async Task Authenticated_image_request_returns_jpeg_with_private_no_store_caching()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        var src = await this.Page.Locator(".gallery-img").First.GetAttributeAsync("src");
        Assert.That(src, Is.Not.Null.And.Not.Empty);

        var response = await this.Page.APIRequest.GetAsync($"{WebAppFixture.BaseUrl}{src}");

        Assert.Multiple(() =>
        {
            Assert.That(response.Ok, Is.True);
            Assert.That(response.Headers["content-type"], Is.EqualTo("image/jpeg"));
            Assert.That(response.Headers["cache-control"], Is.EqualTo("private, no-store"));
        });
        var bytes = await response.BodyAsync();
        Assert.That(bytes, Is.Not.Empty);
    }

    [Test]
    public async Task Unknown_property_image_returns_404()
    {
        await this.LoginAsAdmin();

        var response = await this.Page.APIRequest.GetAsync($"{WebAppFixture.BaseUrl}/images/{Guid.NewGuid()}/thumb.jpg");

        Assert.That(response.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Path_traversal_image_request_returns_404()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();

        var response = await this.Page.APIRequest.GetAsync(
            $"{WebAppFixture.BaseUrl}/images/{this.Seed.BeachId}/..%2f..%2fplaceholder.jpg");

        Assert.That(response.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Guest_door_code_persists()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#door-codes");

        var guestRow = this.Page.Locator(".form-inline").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.Locator("input[name=doorCode]").FillAsync("9999");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save All" }).ClickAsync();

        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#door-codes");
        var reloadedRow = this.Page.Locator(".form-inline").Filter(new() { HasText = this.Seed.GuestEmail });
        await this.Expect(reloadedRow.Locator("input[name=doorCode]")).ToHaveValueAsync("9999");
    }

    [Test]
    public async Task Add_and_remove_favorite_spot()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#favorite-places");

        await this.Page.Locator("#spot-name").FillAsync("New Spot");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Place" }).ClickAsync();

        var card = this.Page.Locator(".spot-card").Filter(new() { HasText = "New Spot" });
        await this.Expect(card).ToBeVisibleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await this.Expect(this.Page.Locator(".spot-card").Filter(new() { HasText = "New Spot" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Add_and_remove_walking_path()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });

        // One point isn't a path, and the form knows it.
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });

        await this.Page.Locator("#path-name").FillAsync("New Path");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync();

        var card = this.Page.Locator(".path-card").Filter(new() { HasText = "New Path" });
        await this.Expect(card).ToBeVisibleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Remove" }).ClickAsync();
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "New Path" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Edit_walking_path_updates_name_and_coordinates()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("Original Path");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync();

        var card = this.Page.Locator(".path-card").Filter(new() { HasText = "Original Path" });
        await this.Expect(card).ToBeVisibleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await this.Expect(this.Page.Locator("#path-name")).ToHaveValueAsync("Original Path");
        await this.Expect(this.Page.Locator("#path-submit-btn")).ToHaveTextAsync("Save Path");

        await this.Page.Locator("#path-name").FillAsync("Renamed Path");
        await map.ClickAsync(new() { Position = new() { X = 200, Y = 160 } });
        await this.Page.Locator("#path-submit-btn").ClickAsync();

        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Renamed Path" })).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Original Path" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Cancel_editing_a_walking_path_resets_the_form()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("Cancel Test Path");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync();

        var card = this.Page.Locator(".path-card").Filter(new() { HasText = "Cancel Test Path" });
        await this.Expect(card).ToBeVisibleAsync();

        await card.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel edit" })).ToBeVisibleAsync();
        await this.Page.Locator("#path-name").FillAsync("Should Not Be Saved");

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel edit" }).ClickAsync();
        await this.Expect(this.Page.Locator("#path-name")).ToHaveValueAsync("");
        await this.Expect(this.Page.Locator("#path-submit-btn")).ToHaveTextAsync("+ Add Path");
        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Cancel edit" })).Not.ToBeVisibleAsync();

        await this.GotoResilientAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Cancel Test Path" })).ToBeVisibleAsync();
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Should Not Be Saved" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Can_delete_a_property()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/properties");

        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.CabinName });
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.CabinName })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Deleting_a_property_removes_every_dependent_type_and_its_images()
    {
        await this.LoginAsAdmin();
        await this.UploadPhotoAsync();
        var imageSrc = await this.Page.Locator(".gallery-img").First.GetAttributeAsync("src");
        Assert.That(imageSrc, Is.Not.Null.And.Not.Empty);

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Door Codes" }).ClickAsync();
        var guestRow = this.Page.Locator(".form-inline").Filter(new() { HasText = this.Seed.GuestEmail });
        await guestRow.Locator("input[name=doorCode]").FillAsync("4321");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Save All" }).ClickAsync();

        await this.Page.GotoAsync("/admin/blackouts");
        var blackoutDate = DateOnly.FromDateTime(DateTime.Today).AddDays(30).ToString("yyyy-MM-dd");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Blackout" }).ClickAsync();
        await this.Page.Locator("#start_date").FillAsync(blackoutDate);
        await this.Page.Locator("#end_date").FillAsync(blackoutDate);
        await this.Page.Locator("input[name=reason]").FillAsync("Pre-deletion test blackout");
        await this.Page.Locator("select[name=propertyId]").SelectOptionAsync(new SelectOptionValue { Label = this.Seed.BeachName });
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true }).ClickAsync();
        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = blackoutDate })).ToBeVisibleAsync();

        var seedResp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-guestbook-reservation");
        Assert.That(seedResp.Ok, Is.True);

        await this.LoginAsGuest();
        await this.Page.GotoAsync("/reservations");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" }).ClickAsync();
        var writeModal = this.Page.Locator("dialog[x-ref='guestbookModal']");
        await this.Expect(writeModal).ToBeVisibleAsync();
        await writeModal.Locator("#guestbook-message").FillAsync("Great stay, deleting this property next.");
        await this.Page.RunAndWaitForResponseAsync(
            () => writeModal.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync(),
            r => r.Request.IsNavigationRequest && r.Url.Contains("/reservations"));
        await this.Page.WaitForLoadStateAsync(LoadState.Load);

        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/properties");
        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        var row = this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.BeachName });
        await row.GetByRole(AriaRole.Button, new() { Name = "Delete" }).ClickAsync();

        await this.Expect(this.Page.Locator(".admin-row").Filter(new() { HasText = this.Seed.BeachName })).ToHaveCountAsync(0);

        var imageResponse = await this.Page.APIRequest.GetAsync($"{WebAppFixture.BaseUrl}{imageSrc}");
        Assert.That(imageResponse.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Overlong_property_name_is_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync("/admin/properties");

        await this.Page.GetByRole(AriaRole.Button, new() { Name = "New Property" }).ClickAsync();
        await this.Page.Locator("input[name=name]").FillAsync(new string('x', 201));
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Add Property" }).ClickAsync();

        await this.Expect(this.Page.Locator(".modal-error")).ToContainTextAsync("too long");
        await this.Expect(this.Page).ToHaveURLAsync(new Regex(@"/admin/properties$"));
    }

    [Test]
    public async Task Overlong_walking_path_name_is_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync(new string('x', 201));

        var response = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync(),
            r => r.Url.Contains("handler=AddPath"));
        Assert.That(response.Status, Is.EqualTo(400));

        await this.Expect(this.Page.Locator("#path-error")).ToContainTextAsync("too long");
        await this.Expect(this.Page.Locator(".paths-list")).Not.ToContainTextAsync(new string('x', 201));
    }

    [Test]
    public async Task Blank_walking_path_name_is_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("   ");

        var response = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync(),
            r => r.Url.Contains("handler=AddPath"));
        Assert.That(response.Status, Is.EqualTo(400));

        await this.Expect(this.Page.Locator("#path-error")).ToContainTextAsync("required");
        await this.Expect(this.Page.Locator(".paths-list")).Not.ToContainTextAsync("Unnamed Path");
    }

    [Test]
    public async Task Walking_path_weight_out_of_range_is_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("Bad Weight Path");
        await this.Page.Locator("#path-weight").FillAsync("13");

        var response = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync(),
            r => r.Url.Contains("handler=AddPath"));
        Assert.That(response.Status, Is.EqualTo(400));

        await this.Expect(this.Page.Locator("#path-error")).ToContainTextAsync("between");
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Bad Weight Path" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Fractional_walking_path_weight_is_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("Fractional Weight Path");
        await this.Page.Locator("#path-weight").FillAsync("1.5");

        var response = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync(),
            r => r.Url.Contains("handler=AddPath"));
        Assert.That(response.Status, Is.EqualTo(400));

        await this.Expect(this.Page.Locator("#path-error")).ToContainTextAsync("between");
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Fractional Weight Path" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Malformed_walking_path_coordinates_are_rejected()
    {
        await this.LoginAsAdmin();
        await this.Page.GotoAsync($"/admin/properties/{this.Seed.BeachId}/edit#walking-paths");

        var map = this.Page.Locator("#path-drawing-map.leaflet-container");
        await this.Expect(map).ToBeVisibleAsync(new() { Timeout = 15000 });
        await map.ClickAsync(new() { Position = new() { X = 80, Y = 80 } });
        await map.ClickAsync(new() { Position = new() { X = 160, Y = 120 } });
        await this.Page.Locator("#path-name").FillAsync("Out Of Range Path");
        await this.Page.Locator("#path-coordinates").EvaluateAsync("el => el.value = '[[999,0],[0,0]]'");

        var response = await this.Page.RunAndWaitForResponseAsync(
            () => this.Page.GetByRole(AriaRole.Button, new() { Name = "+ Add Path" }).ClickAsync(),
            r => r.Url.Contains("handler=AddPath"));
        Assert.That(response.Status, Is.EqualTo(400));

        await this.Expect(this.Page.Locator("#path-error")).ToContainTextAsync("Latitude");
        await this.Expect(this.Page.Locator(".path-card").Filter(new() { HasText = "Out Of Range Path" })).ToHaveCountAsync(0);
    }
}
