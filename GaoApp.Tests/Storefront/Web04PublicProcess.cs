using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace GaoApp.Tests.Storefront;

// Runs only the separate customer process against a guarded GUID database.
// No admin Program, seeder, hosted jobs, real uploads or user key ring is loaded.
internal sealed class Web04PublicProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output = new();
    private readonly string log;
    internal HttpClient Http { get; }
    internal string LogPath => log;
    internal string BaseUrl => Http.BaseAddress!.ToString().TrimEnd('/');
    private Web04PublicProcess(Process process, int port, string log)
    {
        this.process = process; this.log = log;
        Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); }; process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
    }
    internal static async Task<Web04PublicProcess> StartAsync(Web04Case c, int? fixedPort = null, string environment = "Development", bool preview = true,
        string? host = null, string? subdomain = null, bool keys = true, string? uploads = null)
    {
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = fixedPort ?? ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var runtime = Path.Combine(c.Directory, "host-" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(runtime, "WEB04 owned public host"); Directory.CreateDirectory(runtime);
        var dll = Environment.GetEnvironmentVariable("GAOAPP_WEB04_PUBLIC_DLL") ?? Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/build/bin/GaoApp.Storefront.Web/debug/GaoApp.Storefront.Web.dll");
        Assert.True(File.Exists(dll), "Root must freeze/build WEB04 before executing tests.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(dll)); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Storefront.Web"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment; start.Environment["DOTNET_ENVIRONMENT"] = environment; start.Environment["ConnectionStrings__DefaultConnection"] = c.Seed.Database.ConnectionString;
        start.Environment["StorefrontHost__Host"] = host ?? "127.0.0.1"; start.Environment["StorefrontHost__StoreSubdomain"] = subdomain ?? await c.Seed.SubdomainAsync();
        start.Environment["StorefrontHost__PreviewEnabled"] = preview.ToString(); start.Environment["StorefrontHost__UploadsRoot"] = uploads ?? Path.Combine(runtime, "uploads");
        start.Environment["StorefrontHost__KeyRingPath"] = keys ? c.KeysDirectory : ""; start.Environment["StorefrontAddressCatalog__Path"] = c.AddressFile;
        var process = new Process { StartInfo = start }; var fixture = new Web04PublicProcess(process, port, Path.Combine(runtime, "public-host.log"));
        try
        {
            Assert.True(process.Start()); StorefrontTestResourceMetadata.Producer(process, keys ? c.KeysDirectory : null, new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog); await File.WriteAllTextAsync(Path.Combine(runtime, "owned-process.json"), System.Text.Json.JsonSerializer.Serialize(new { process.Id, Started = DateTime.UtcNow, dll = Path.GetFullPath(dll), GuardedDatabase = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog }));
            process.BeginOutputReadLine(); process.BeginErrorReadLine(); var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (process.HasExited) throw new InvalidOperationException("Owned WEB04 public host exited: " + string.Join('\n', fixture.output));
                try { using var health = await fixture.Http.GetAsync("/health"); if (health.IsSuccessStatusCode) return fixture; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Owned WEB04 public host startup timed out.");
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    internal async Task<string> CsrfAsync(string route = "/shop/orders")
    {
        var html = await Http.GetStringAsync(route); var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Actual public Razor must issue its own CSRF token."); return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    public async ValueTask DisposeAsync()
    {
        Http.Dispose(); if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await File.WriteAllLinesAsync(log, output); process.Dispose();
    }
}
