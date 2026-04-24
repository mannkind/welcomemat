using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[SetUpFixture]
public class WebAppFixture
{
    public static string BaseUrl { get; private set; } = "";
    public static string StatePath { get; private set; } = "";

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Structure",
        "NUnit1032:An IDisposable field/property should be Disposed in a TearDown method",
        Justification = "The process is disposed both during restart and from OneTimeTearDown through StopProcess.")]
    private static Process? App;
    private static string AppDll = "";
    private static string ProjectDir = "";
    private static int Port;

    [OneTimeSetUp]
    public async Task StartAsync()
    {
        var repoRoot = FindRepoRoot();
        var config = AppContext.BaseDirectory.Replace('\\', '/').Contains("/Release/") ? "Release" : "Debug";
        ProjectDir = Path.Combine(repoRoot, "WelcomeMat");
        AppDll = Path.Combine(ProjectDir, "bin", config, "net10.0", "WelcomeMat.dll");
        if (!File.Exists(AppDll))
        {
            throw new FileNotFoundException(
                $"WelcomeMat app not built at {AppDll}. Build it ({config}) before running E2E tests.");
        }

        StatePath = Path.Combine(Path.GetTempPath(), $"welcomemat-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(StatePath);
        Port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{Port}";

        await StartProcessAsync();
    }

    public static async Task RestartAsync()
    {
        StopProcess();
        await StartProcessAsync();
    }

    private static async Task StartProcessAsync()
    {
        var dotnet = Environment.ProcessPath is { } p &&
                     Path.GetFileNameWithoutExtension(p).Equals("dotnet", StringComparison.OrdinalIgnoreCase)
            ? p
            : "dotnet";

        var psi = new ProcessStartInfo
        {
            FileName = dotnet,
            WorkingDirectory = ProjectDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(AppDll);
        psi.Environment["WELCOMEMAT_TEST_AUTH"] = "1";
        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Testing";
        psi.Environment["STATE_PATH"] = StatePath;
        psi.Environment["DATABASE_PATH"] = Path.Combine(StatePath, "reservations.db");
        psi.Environment["LISTEN_ADDR"] = $"127.0.0.1:{Port}";
        psi.Environment["BASE_URL"] = BaseUrl;
        psi.Environment["GOOGLE_CLIENT_ID"] = "test-client-id";
        psi.Environment["GOOGLE_CLIENT_SECRET"] = "test-client-secret";

        App = new Process { StartInfo = psi };
        App.OutputDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app] {e.Data}"); } };
        App.ErrorDataReceived += (_, e) => { if (e.Data is not null) { TestContext.Progress.WriteLine($"[app:err] {e.Data}"); } };
        App.Start();
        App.BeginOutputReadLine();
        App.BeginErrorReadLine();

        await WaitForReadyAsync();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        StopProcess();
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

    private static void StopProcess()
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
        App = null;
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
