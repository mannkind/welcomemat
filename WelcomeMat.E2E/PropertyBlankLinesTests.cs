using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class PropertyBlankLinesTests : TestBase
{
    [Test]
    public async Task Checkin_steps_skip_whitespace_only_lines()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-current-reservation");
        Assert.That(resp.Ok, Is.True, "POST /test-support/seed-current-reservation should succeed");

        await this.LoginAsGuest();
        await this.Page.GotoAsync($"/property/{this.Seed.BeachId}");

        var card = this.Page.Locator(".quick-card").Filter(new() { HasText = "Check-in Steps" });
        var items = card.Locator("li");

        await this.Expect(items).ToHaveCountAsync(2);
        await this.Expect(items.Nth(0)).ToHaveTextAsync("Pick up keys from lockbox");
        await this.Expect(items.Nth(1)).ToHaveTextAsync("Code is the door code");
    }
}
