using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace WelcomeMat.E2E;

public record SeedData
{
    public string AdminEmail { get; init; } = string.Empty;

    public string GuestEmail { get; init; } = string.Empty;

    public string Guest2Email { get; init; } = string.Empty;

    public string AdminCalToken { get; init; } = string.Empty;

    public string GuestCalToken { get; init; } = string.Empty;

    public string BeachIcalToken { get; init; } = string.Empty;

    public string BeachId { get; init; } = string.Empty;

    public string BeachName { get; init; } = string.Empty;

    public string CabinId { get; init; } = string.Empty;

    public string CabinName { get; init; } = string.Empty;

    public string ReservationId { get; init; } = string.Empty;

    public string ReservationCheckIn { get; init; } = string.Empty;

    public string ReservationCheckOut { get; init; } = string.Empty;
}

public abstract class TestBase : PageTest
{
    protected SeedData Seed { get; private set; } = null!;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = WebAppFixture.BaseUrl,
        IgnoreHTTPSErrors = true,
    };

    [SetUp]
    public async Task ResetAndSeedAsync()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/reset");
        Assert.That(resp.Ok, Is.True, "POST /test-support/reset should succeed");
        var json = (await resp.JsonAsync())!.Value;
        this.Seed = new SeedData
        {
            AdminEmail = Str(json, "adminEmail"),
            GuestEmail = Str(json, "guestEmail"),
            Guest2Email = Str(json, "guest2Email"),
            AdminCalToken = Str(json, "adminCalToken"),
            GuestCalToken = Str(json, "guestCalToken"),
            BeachIcalToken = Str(json, "beachIcalToken"),
            BeachId = Str(json, "beachId"),
            BeachName = Str(json, "beachName"),
            CabinId = Str(json, "cabinId"),
            CabinName = Str(json, "cabinName"),
            ReservationId = Str(json, "reservationId"),
            ReservationCheckIn = Str(json, "reservationCheckIn"),
            ReservationCheckOut = Str(json, "reservationCheckOut"),
        };
    }

    protected Task LoginAsAdmin() => this.LoginAsync(this.Seed.AdminEmail);
    protected Task LoginAsGuest() => this.LoginAsync(this.Seed.GuestEmail);

    protected Task LoginAsync(string email) =>
        this.GotoResilientAsync($"/test-auth/login?email={Uri.EscapeDataString(email)}");

    protected async Task GotoResilientAsync(string url)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await this.Page.GotoAsync(url);
                return;
            }
            catch (PlaywrightException ex) when (
                attempt < maxAttempts && ex.Message.Contains("interrupted by another navigation"))
            {
                await Task.Delay(100 * attempt);
            }
        }
    }

    private static string Str(JsonElement e, string name) => e.GetProperty(name).GetString()!;
}
