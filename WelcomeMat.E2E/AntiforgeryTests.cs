using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class AntiforgeryTests : TestBase
{
    [Test]
    public async Task State_changing_request_without_antiforgery_token_is_rejected()
    {
        await this.LoginAsGuest();

        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/impersonate/stop");

        Assert.That(resp.Status, Is.EqualTo(400),
            "an authenticated POST without an antiforgery token must be rejected");
    }
}
