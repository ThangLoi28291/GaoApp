using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GaoApp.Tests.Storefront;

internal sealed class Web05PublicProcess(Process process, int port, string log) : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> output = new();
    private bool started;
    internal HttpClient Http { get; } = new(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() }) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
    internal string BaseUrl => Http.BaseAddress!.ToString().TrimEnd('/');
    internal string LogPath => log;
    internal static async Task<Web05PublicProcess> StartAsync(Web05Case c, string environment = "Development", bool preview = true, bool registration = true)
    {
        var database = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(c.Seed.Database.ConnectionString).InitialCatalog;
        Assert.Matches("^GaoApp_R2_InventoryPosting_[A-F0-9]{32}$", database);
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var runtime = Path.Combine(c.Web.Directory, "host-" + Guid.NewGuid().ToString("N")); StorefrontTestResourceMetadata.BeforeDirectory(runtime, "WEB05 owned public host"); Directory.CreateDirectory(runtime);
        var dll = Environment.GetEnvironmentVariable("GAOAPP_WEB05_PUBLIC_DLL") ?? Path.Combine(Web03Support.Root(), ".artifacts/storefront-rollout/build/bin/GaoApp.Storefront.Web/debug/GaoApp.Storefront.Web.dll");
        Assert.True(File.Exists(dll), "Root must freeze/build WEB05 before executing tests.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(dll)); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(Path.Combine(Web03Support.Root(), "GaoApp.Storefront.Web"));
        start.Environment["ASPNETCORE_ENVIRONMENT"] = environment; start.Environment["DOTNET_ENVIRONMENT"] = environment;
        start.Environment["ConnectionStrings__DefaultConnection"] = c.Seed.Database.ConnectionString; start.Environment["StorefrontHost__Host"] = "127.0.0.1";
        start.Environment["StorefrontHost__StoreSubdomain"] = await c.Seed.SubdomainAsync(); start.Environment["StorefrontHost__PreviewEnabled"] = preview.ToString();
        start.Environment["StorefrontHost__UploadsRoot"] = Path.Combine(runtime, "uploads"); start.Environment["StorefrontHost__KeyRingPath"] = c.Web.KeysDirectory;
        start.Environment["StorefrontAddressCatalog__Path"] = c.Web.AddressFile; start.Environment["StorefrontBanking__WorkerEnabled"] = "false";
        var prefix = "StorefrontBanking__Registrations__" + c.StoreId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "__";
        start.Environment[prefix + "NamespaceVerified"] = registration.ToString(); start.Environment[prefix + "Namespace"] = "W"; start.Environment[prefix + "Provider"] = "ACB";
        start.Environment[prefix + "EvidenceReference"] = c.Registration.EvidenceReference; start.Environment[prefix + "MerchantId"] = c.Registration.MerchantId;
        start.Environment[prefix + "TerminalCode"] = c.Registration.TerminalCode; start.Environment[prefix + "UserCode"] = c.Registration.UserCode;
        start.Environment[prefix + "BankAccountId"] = c.Registration.BankAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[prefix + "ReceivingLegalEntityId"] = c.Registration.ReceivingLegalEntityId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        start.Environment[prefix + "AccountIdentity"] = c.Registration.AccountIdentity;
        var owned = new Process { StartInfo = start }; var result = new Web05PublicProcess(owned, port, Path.Combine(runtime, "public-host.log"));
        owned.OutputDataReceived += (_, e) => { if (e.Data is not null) result.output.Enqueue(e.Data); }; owned.ErrorDataReceived += (_, e) => { if (e.Data is not null) result.output.Enqueue(e.Data); };
        try
        {
            Assert.True(owned.Start()); StorefrontTestResourceMetadata.Producer(owned, c.Web.KeysDirectory, database); result.started = true; await File.WriteAllTextAsync(Path.Combine(runtime, "owned-process.json"), JsonSerializer.Serialize(new { owned.Id, Started = DateTime.UtcNow, DLL = Path.GetFullPath(dll), GuardedDatabase = database }));
            owned.BeginOutputReadLine(); owned.BeginErrorReadLine(); var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (owned.HasExited) throw new InvalidOperationException("Owned WEB05 public host exited: " + string.Join('\n', result.output));
                try { using var health = await result.Http.GetAsync("/health"); if (health.IsSuccessStatusCode) return result; } catch (HttpRequestException) { }
                await Task.Delay(100);
            }
            throw new TimeoutException("Owned WEB05 public host startup timed out.");
        }
        catch { await result.DisposeAsync(); throw; }
    }
    internal async Task CsrfAsync()
    {
        var html = await Http.GetStringAsync("/shop/orders"); var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success); Http.DefaultRequestHeaders.Remove("RequestVerificationToken"); Http.DefaultRequestHeaders.Add("RequestVerificationToken", WebUtility.HtmlDecode(match.Groups[1].Value));
    }
    public async ValueTask DisposeAsync()
    {
        Http.Dispose(); if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        await File.WriteAllLinesAsync(log, output); process.Dispose();
    }
}
