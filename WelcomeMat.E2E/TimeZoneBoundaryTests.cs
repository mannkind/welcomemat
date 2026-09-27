using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class TimeZoneBoundaryTests : PageTest
{
    private const string FROZEN_UTC = "2026-01-15T02:00:00Z";

    private static string BaseUrl = "";
    private static string StatePath = "";
    private static Process? App;
    private string GuestEmail = "";

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = BaseUrl,
        IgnoreHTTPSErrors = true,
    };

    [OneTimeSetUp]
    public async Task StartAppAsync()
    {
        var repoRoot = FindRepoRoot();
        var config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        var projectDir = Path.Combine(repoRoot, "WelcomeMat");
        var appDll = Path.Combine(projectDir, "bin", config, "net10.0", "WelcomeMat.dll");
        if (!File.Exists(appDll))
        {
            throw new FileNotFoundException($"WelcomeMat app not built at {appDll}. Build it ({config}) before running E2E tests.");
        }

        StatePath = Path.Combine(Path.GetTempPath(), $"welcomemat-e2e-tz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StatePath);
        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}";

        var dotnet = Environment.ProcessPath is { } p &&
                     Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p
            : "dotnet";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(appDll);
        psi.Environment["WELCOMEMAT_TEST_AUTH"] = "1";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        psi.Environment["STATE_PATH"] = StatePath;
        psi.Environment["DATABASE_PATH"] = Path.Combine(StatePath, "reservations.db");
        psi.Environment["LISTEN_ADDR"] = $"127.0.0.1:{port}";
        psi.Environment["BASE_URL"] = BaseUrl;
        psi.Environment["GOOGLE_CLIENT_ID"] = "test-client-id";
        psi.Environment["GOOGLE_CLIENT_SECRET"] = "test-client-secret";
        psi.Environment["WELCOMEMAT_TEST_FROZEN_UTC"] = FROZEN_UTC;

        App = new Process { StartInfo = psi };
        App.OutputDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[tz-app] {e.Data}"); } };
        App.ErrorDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[tz-app:err] {e.Data}"); } };
        App.Start();
        App.BeginOutputReadLine();
        App.BeginErrorReadLine();

        await WaitForReadyAsync();
    }

    [OneTimeTearDown]
    public void StopApp()
    {
        try
        {
            if (App is { HasExited: false })
            {
                App.Kill(entireProcessTree: true);
                App.WaitForExit();
            }
        }
        catch { /* best effort */ }
        App?.Dispose();
        try
        {
            if (Directory.Exists(StatePath))
            {
                Directory.Delete(StatePath, recursive: true);
            }
        }
        catch { /* best effort */ }
    }

    [SetUp]
    public async Task ResetAndSeedAsync()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{BaseUrl}/test-support/reset");
        Assert.That(resp.Ok, Is.True, "POST /test-support/reset should succeed");
        var json = (await resp.JsonAsync())!.Value;
        this.GuestEmail = json.GetProperty("guestEmail").GetString()!;
    }

    private async Task LoginAsGuestAsync() =>
        await this.Page.GotoAsync($"/test-auth/login?email={Uri.EscapeDataString(this.GuestEmail)}");

    [Test]
    public async Task Calendar_HighlightsPacificToday_NotUtcTomorrow()
    {
        await this.LoginAsGuestAsync();
        await this.Page.GotoAsync("/calendar");

        var todayCell = this.Page.Locator(".day.today .day-number");
        await this.Expect(todayCell).ToHaveCountAsync(1);
        await this.Expect(todayCell).ToHaveTextAsync("14");
    }

    [Test]
    public async Task Create_HonorsPacificAdvanceNoticeBoundary_NotUtc()
    {
        await this.LoginAsGuestAsync();
        await this.Page.GotoAsync("/calendar");

        await this.Page.Locator(".day[data-date='2026-01-15']").ClickAsync();
        await this.Page.Locator(".day[data-date='2026-01-16']").ClickAsync();

        await this.Expect(this.Page.Locator("dialog.modal[open]")).ToBeVisibleAsync();
        var submitTask = this.Page.WaitForResponseAsync(r =>
            r.Url.Contains("/reservations") && r.Request.Method == "POST");
        await this.Page.GetByRole(AriaRole.Button, new() { Name = "Book" }).ClickAsync();
        var resp = await submitTask;

        Assert.That(resp.Status, Is.EqualTo(200),
            "check-in exactly 1 day out (in property-local time) must be accepted");
    }

    [Test]
    public async Task PropertyPage_ShowsStayEndingPacificToday_EvenThoughUtcAlreadyEnded()
    {
        var resetResp = await this.Page.APIRequest.PostAsync($"{BaseUrl}/test-support/reset");
        Assert.That(resetResp.Ok, Is.True);
        var resetJson = (await resetResp.JsonAsync())!.Value;
        var beachId = resetJson.GetProperty("beachId").GetString()!;
        this.GuestEmail = resetJson.GetProperty("guestEmail").GetString()!;

        var seedResp = await this.Page.APIRequest.PostAsync($"{BaseUrl}/test-support/seed-ending-today-reservation");
        Assert.That(seedResp.Ok, Is.True);

        await this.LoginAsGuestAsync();
        await this.Page.GotoAsync($"/property/{beachId}");

        await this.Expect(this.Page.Locator(".stay-card")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Cancel_AllowsReservationEndingPacificTomorrow_NotBlockedByUtcToday()
    {
        var seedResp = await this.Page.APIRequest.PostAsync($"{BaseUrl}/test-support/seed-ending-tomorrow-reservation");
        Assert.That(seedResp.Ok, Is.True);

        await this.LoginAsGuestAsync();
        await this.Page.GotoAsync("/reservations");
        this.Page.Dialog += async (_, dialog) => await dialog.AcceptAsync();

        var row = this.Page.Locator(".reservation-row", new() { HasText = "Jan 15" });
        var deleteTask = this.Page.WaitForResponseAsync(r =>
            r.Url.Contains("/reservations/") && r.Request.Method == "DELETE");
        await row.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        var deleteResp = await deleteTask;

        Assert.That(deleteResp.Status, Is.EqualTo(200));
    }

    [Test]
    public async Task Guestbook_WriteWindowIncludesPacificBoundaryDay_NotExcludedByUtc()
    {
        var seedResp = await this.Page.APIRequest.PostAsync($"{BaseUrl}/test-support/seed-guestbook-boundary-reservation");
        Assert.That(seedResp.Ok, Is.True);

        await this.LoginAsGuestAsync();
        await this.Page.GotoAsync("/reservations");

        await this.Expect(this.Page.GetByRole(AriaRole.Button, new() { Name = "Guestbook" })).ToHaveCountAsync(1);
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WaitForReadyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(60);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            if (App is { HasExited: true })
            {
                throw new InvalidOperationException($"App process exited early (code {App.ExitCode}).");
            }

            try
            {
                var resp = await http.GetAsync($"{BaseUrl}/login");
                if (resp.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(500);
        }
        throw new TimeoutException($"App did not become ready at {BaseUrl}. Last error: {last?.Message}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WelcomeMat.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root (WelcomeMat.slnx).");
    }
}
