using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class HealthTests
{
    [Test]
    public async Task Healthz_ReturnsExactlyStatusHealthy()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync($"{WebAppFixture.BaseUrl}/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Multiple(() =>
        {
            Assert.That(doc.RootElement.EnumerateObject().Select(p => p.Name), Is.EqualTo(new[] { "status" }));
            Assert.That(doc.RootElement.GetProperty("status").GetString(), Is.EqualTo("healthy"));
        });
    }

    [Test]
    public async Task Healthz_IsReachableWithoutAuthentication()
    {
        using var http = new HttpClient();
        var response = await http.GetAsync($"{WebAppFixture.BaseUrl}/healthz");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task Healthz_RejectsSpoofedLoopbackHostHeader_ButAcceptsTheConfiguredBaseUrlHost()
    {
        const string ConfiguredHost = "welcome-healthcheck-test.example";

        await using var app = await LaunchProductionLikeAppAsync(ConfiguredHost);
        using var http = new HttpClient();

        foreach (var spoofedHost in new[] { "localhost", "127.0.0.1", "not-a-configured-host.example" })
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{app.BaseUrl}/healthz");
            request.Headers.Host = spoofedHost;
            var response = await http.SendAsync(request);
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.BadRequest),
                $"a request with Host: {spoofedHost} must still be rejected by host filtering");
        }

        var okRequest = new HttpRequestMessage(HttpMethod.Get, $"{app.BaseUrl}/healthz");
        okRequest.Headers.Host = ConfiguredHost;
        var okResponse = await http.SendAsync(okRequest);
        Assert.That(okResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await okResponse.Content.ReadAsStringAsync();
        Assert.That(body, Is.EqualTo("{\"status\":\"healthy\"}"));
    }

    private class RunningApp : IAsyncDisposable
    {
        public RunningApp(Process process, string statePath)
        {
            this.Process = process;
            this.StatePath = statePath;
        }

        public string BaseUrl { get; init; } = "";

        public ValueTask DisposeAsync()
        {
            try
            {
                if (!this.Process.HasExited)
                {
                    this.Process.Kill(entireProcessTree: true);
                    this.Process.WaitForExit();
                }
            }
            catch { /* best effort */ }
            this.Process.Dispose();
            try
            {
                if (Directory.Exists(this.StatePath))
                {
                    Directory.Delete(this.StatePath, recursive: true);
                }
            }
            catch { /* best effort */ }
            return ValueTask.CompletedTask;
        }


        private readonly Process Process;

        private readonly string StatePath;
    }

    private static async Task<RunningApp> LaunchProductionLikeAppAsync(string configuredHost)
    {
        var repoRoot = FindRepoRoot();
        var config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        var projectDir = Path.Combine(repoRoot, "WelcomeMat");
        var appDll = Path.Combine(projectDir, "bin", config, "net10.0", "WelcomeMat.dll");
        if (!File.Exists(appDll))
        {
            throw new FileNotFoundException($"WelcomeMat app not built at {appDll}. Build it ({config}) before running E2E tests.");
        }

        var dotnet = Environment.ProcessPath is { } p &&
                     Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p
            : "dotnet";

        var statePath = Path.Combine(Path.GetTempPath(), $"welcomemat-e2e-healthz-hostfilter-{Guid.NewGuid():N}");
        Directory.CreateDirectory(statePath);
        var port = GetFreePort();
        var baseUrl = $"http://127.0.0.1:{port}";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = projectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(appDll);
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        psi.Environment["STATE_PATH"] = statePath;
        psi.Environment["DATABASE_PATH"] = Path.Combine(statePath, "reservations.db");
        psi.Environment["LISTEN_ADDR"] = $"127.0.0.1:{port}";
        psi.Environment["BASE_URL"] = $"http://{configuredHost}";
        psi.Environment["GOOGLE_CLIENT_ID"] = "test-client-id";
        psi.Environment["GOOGLE_CLIENT_SECRET"] = "test-client-secret";
        psi.Environment["INITIAL_USER"] = "admin@example.com";

        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app] {e.Data}"); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app:err] {e.Data}"); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForReadyAsync(process, baseUrl, configuredHost);
        return new RunningApp(process, statePath) { BaseUrl = baseUrl };
    }

    private static async Task WaitForReadyAsync(Process process, string baseUrl, string configuredHost)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow.AddSeconds(30);
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"App process exited early (code {process.ExitCode}).");
            }

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/healthz");
                request.Headers.Host = configuredHost;
                var resp = await http.SendAsync(request);
                if (resp.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (Exception ex) { last = ex; }
            await Task.Delay(250);
        }
        throw new TimeoutException($"App did not become ready at {baseUrl}. Last error: {last?.Message}");
    }

    private static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
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
