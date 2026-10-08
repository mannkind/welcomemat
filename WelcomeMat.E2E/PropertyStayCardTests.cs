using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class PropertyStayCardTests : TestBase
{
    [Test]
    public async Task Stay_card_shows_current_stay_through_its_checkout_day()
    {
        await this.LoginAsGuest();

        var resp = await this.Page.APIRequest.PostAsync(
            $"{WebAppFixture.BaseUrl}/test-support/seed-ending-today-reservation");
        Assert.That(resp.Ok, Is.True, "seeding the ending-today reservation should succeed");
        var json = (await resp.JsonAsync())!.Value;
        var checkIn = DateOnly.Parse(json.GetProperty("checkIn").GetString()!);
        var checkOut = DateOnly.Parse(json.GetProperty("checkOut").GetString()!);

        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        var stayCard = this.Page.Locator(".stay-card");
        await this.Expect(stayCard).ToBeVisibleAsync();
        // Asserting on the dates, since both stays render the same card layout.
        await this.Expect(stayCard).ToContainTextAsync(checkIn.ToString("MMM d"));
        await this.Expect(stayCard).ToContainTextAsync(checkOut.ToString("MMM d"));
        await this.Expect(stayCard).Not.ToContainTextAsync(
            DateOnly.Parse(this.Seed.ReservationCheckIn).ToString("MMM d"));
    }
}
