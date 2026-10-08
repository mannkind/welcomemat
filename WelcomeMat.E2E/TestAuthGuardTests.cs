using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class TestAuthGuardTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ProjectDir = Path.Combine(RepoRoot, "WelcomeMat");
    private static readonly string AppDll = ResolveAppDll();

    [Test]
    public async Task Production_WithTestAuthSpecified_FailsFastWithTheExactConfigurationError()
    {
        var result = await RunToCompletionAsync(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["WELCOMEMAT_TEST_AUTH"] = "1",
            ["GOOGLE_CLIENT_ID"] = "client",
            ["GOOGLE_CLIENT_SECRET"] = "secret",
            ["BASE_URL"] = "https://welcome.example.com",
            ["INITIAL_USER"] = "admin@example.com",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StdErr, Does.Contain("WELCOMEMAT_TEST_AUTH is only valid in Testing."));
        });
    }

    [Test]
    public async Task Production_FreshInstallationWithoutInitialUser_FailsFastWithAnActionableError()
    {
        var result = await RunToCompletionAsync(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["GOOGLE_CLIENT_ID"] = "client",
            ["GOOGLE_CLIENT_SECRET"] = "secret",
            ["BASE_URL"] = "https://welcome.example.com",
            // Empty state and no INITIAL_USER; a Production install with no way in.
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.ExitCode, Is.Not.EqualTo(0));
            Assert.That(result.StdErr, Does.Contain(
                "INITIAL_USER is required to bootstrap a new installation"));
        });
    }

    [Test]
    public async Task Development_DoesNotMapTestAuthEndpoints()
    {
        await using var app = await LaunchAsync(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Development",
            ["GOOGLE_CLIENT_ID"] = "client",
            ["GOOGLE_CLIENT_SECRET"] = "secret",
            ["INITIAL_USER"] = "admin@example.com",
        });

        using var http = new HttpClient();
        var response = await http.GetAsync($"{app.BaseUrl}/test-auth/login?email=admin@example.com");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Production_DoesNotMapTestAuthEndpoints()
    {
        await using var app = await LaunchAsync(new Dictionary<string, string>
        {
            ["ASPNETCORE_ENVIRONMENT"] = "Production",
            ["GOOGLE_CLIENT_ID"] = "client",
            ["GOOGLE_CLIENT_SECRET"] = "secret",
            ["INITIAL_USER"] = "admin@example.com",
        });

        using var http = new HttpClient();
        var response = await http.GetAsync($"{app.BaseUrl}/test-auth/login?email=admin@example.com");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    private record ProcessResult
    {
        public int ExitCode { get; init; }

        public string StdOut { get; init; } = string.Empty;

        public string StdErr { get; init; } = string.Empty;
    }

    private static async Task<ProcessResult> RunToCompletionAsync(Dictionary<string, string> env)
    {
        var (psi, statePath, _) = BuildStartInfo(env);
        using var process = new Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { stdout.AppendLine(e.Data); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { stderr.AppendLine(e.Data); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var exited = await Task.Run(() => process.WaitForExit(30_000));
        try
        {
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail("App process did not exit within the timeout; expected it to fail fast during startup.");
            }
        }
        finally
        {
            TryDeleteDirectory(statePath);
        }

        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StdOut = stdout.ToString(),
            StdErr = stderr.ToString(),
        };
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
            TryDeleteDirectory(this.StatePath);
            return ValueTask.CompletedTask;
        }


        private readonly Process Process;

        private readonly string StatePath;
    }

    private static async Task<RunningApp> LaunchAsync(Dictionary<string, string> env)
    {
        var (psi, statePath, baseUrl) = BuildStartInfo(env);
        var process = new Process { StartInfo = psi };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app] {e.Data}"); } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app:err] {e.Data}"); } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await WaitForReadyAsync(process, baseUrl);
        return new RunningApp(process, statePath) { BaseUrl = baseUrl };
    }

    private static (ProcessStartInfo Psi, string StatePath, string BaseUrl) BuildStartInfo(
        Dictionary<string, string> env)
    {
        var dotnet = Environment.ProcessPath is { } p &&
                     Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p
            : "dotnet";

        var statePath = Path.Combine(Path.GetTempPath(), $"welcomemat-e2e-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(statePath);
        var port = GetFreePort();
        var defaultBaseUrl = $"http://127.0.0.1:{port}";
        var baseUrl = env.GetValueOrDefault("BASE_URL", defaultBaseUrl);

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(AppDll);
        psi.Environment["STATE_PATH"] = statePath;
        psi.Environment["DATABASE_PATH"] = Path.Combine(statePath, "reservations.db");
        psi.Environment["LISTEN_ADDR"] = $"127.0.0.1:{port}";
        psi.Environment["BASE_URL"] = defaultBaseUrl;
        foreach (var (key, value) in env)
        {
            psi.Environment[key] = value;
        }

        return (psi, statePath, baseUrl);
    }

    private static async Task WaitForReadyAsync(Process process, string baseUrl)
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
                var resp = await http.GetAsync($"{baseUrl}/login");
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch { /* best effort */ }
    }

    private static string ResolveAppDll()
    {
        var config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        var dll = Path.Combine(ProjectDir, "bin", config, "net10.0", "WelcomeMat.dll");
        if (!File.Exists(dll))
        {
            throw new FileNotFoundException($"WelcomeMat app not built at {dll}. Build it ({config}) before running E2E tests.");
        }

        return dll;
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
