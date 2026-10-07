using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[Explicit("Generates README images, not assertions.")]
[Parallelizable(ParallelScope.None)]
public class Screenshots : TestBase
{
    private const int WIDTH = 1280;
    private const int PROBE_HEIGHT = 900;

    public override BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = WebAppFixture.BaseUrl,
        IgnoreHTTPSErrors = true,
        ViewportSize = new ViewportSize { Width = WIDTH, Height = PROBE_HEIGHT },
        DeviceScaleFactor = 2,
        ColorScheme = ColorScheme.Light,
    };

    [Test]
    public async Task Capture()
    {
        var outDir = Path.Combine(SolutionRoot(), "docs", "screenshots");
        Directory.CreateDirectory(outDir);

        var demo = await this.SeedDemoAsync();
        var beachId = demo.GetProperty("beachId").GetString();

        await this.LoginAsync(demo.GetProperty("adminEmail").GetString()!);

        await this.ShotAsync($"/calendar/{beachId}", Path.Combine(outDir, "calendar.png"));
        await this.ShotAsync($"/property/{beachId}", Path.Combine(outDir, "property.jpg"), this.SettleMapAsync);
        await this.ShotAsync("/reservations", Path.Combine(outDir, "reservations.png"));
        await this.ShotAsync("/admin/users", Path.Combine(outDir, "admin.png"));

        TestContext.Out.WriteLine($"Screenshots written to {outDir}");
    }

    private async Task<JsonElement> SeedDemoAsync()
    {
        var resp = await this.Page.APIRequest.PostAsync($"{WebAppFixture.BaseUrl}/test-support/seed-demo");
        Assert.That(resp.Ok, Is.True, "POST /test-support/seed-demo should succeed");
        return (await resp.JsonAsync())!.Value;
    }

    private async Task SettleMapAsync()
    {
        await this.Expect(this.Page.Locator("#property-map")).ToBeVisibleAsync();
        await this.Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await this.Page.WaitForTimeoutAsync(2500);
    }

    private async Task ShotAsync(string url, string file, Func<Task>? settle = null)
    {
        await this.Page.SetViewportSizeAsync(WIDTH, PROBE_HEIGHT);
        await this.GotoResilientAsync(url);
        if (settle is not null)
        {
            await settle();
        }

        var height = Math.Clamp(await this.ContentHeightAsync(), 360, 2400);
        if (height != PROBE_HEIGHT)
        {
            await this.Page.SetViewportSizeAsync(WIDTH, height);
            await this.GotoResilientAsync(url);
            if (settle is not null)
            {
                await settle();
            }
        }

        await this.Page.AddStyleTagAsync(new PageAddStyleTagOptions
        {
            Content = ".day-detail-panel:not(.panel-open), .edit-bar { display: none !important; }",
        });

        // Otherwise whichever cell the pointer last sat over stays highlighted.
        await this.Page.Mouse.MoveAsync(0, 0);
        await this.Page.EvaluateAsync("() => document.fonts.ready");
        await this.Page.WaitForTimeoutAsync(250);
        await this.Page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = file,
            FullPage = true,
            Quality = file.EndsWith(".jpg", StringComparison.Ordinal) ? 85 : null,
        });
    }

    private Task<int> ContentHeightAsync() => this.Page.EvaluateAsync<int>("""
        () => {
            const main = document.querySelector('main') ?? document.body;
            return Math.ceil(main.getBoundingClientRect().bottom + window.scrollY) + 20;
        }
        """);

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WelcomeMat.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the solution root.");
    }
}
