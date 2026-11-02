using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb01"), Trait("Category", "StorefrontWeb01")]
public sealed class Web01PublicHostTests(Web01Fixture fixture)
{
    [Fact]
    public void Customer_host_has_its_own_build_and_no_admin_app_dependency()
    {
        var dll = Web01PublicProcess.DllPath(); Assert.True(File.Exists(dll));
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(dll, ".deps.json")));
        var libraries = deps.RootElement.GetProperty("libraries").EnumerateObject().Select(x => x.Name).ToArray();
        Assert.Contains(libraries, x => x.StartsWith("GaoApp.Storefront.Web/", StringComparison.Ordinal));
        Assert.Contains(libraries, x => x.StartsWith("GaoApp.Infrastructure/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, x => x.StartsWith("GaoApp.Web/", StringComparison.Ordinal));
        Assert.DoesNotContain(libraries, x => x.StartsWith("GaoApp.Migrator/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Separate_customer_process_health_works_without_database_and_keeps_storefront_closed()
    {
        await using var host = await Web01PublicProcess.StartAsync("", "", "");
        using var health = await host.Http.GetAsync("/health"); Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        using var json = JsonDocument.Parse(await health.Content.ReadAsStringAsync());
        Assert.Equal("GaoApp.Storefront.Web", json.RootElement.GetProperty("service").GetString());
        foreach (var path in new[] { "/", "/shop", "/shop?tenant=forged&storeId=999" })
        {
            using var response = await host.Http.GetAsync(path); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore); Assert.Contains("đang chuẩn bị", await response.Content.ReadAsStringAsync());
        }
        foreach (var path in new[] { "/admin", "/admin/pos", "/admin/storefront/settings", "/shop/checkout", "/api/orders" })
        { using var response = await host.Http.GetAsync(path); Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); }
    }

    [Fact]
    public async Task Configured_host_binds_only_its_store_and_has_no_startup_seed_migration_or_writes()
    {
        var c = await fixture.CaseAsync(); string subdomain; string before;
        await using (var db = fixture.Database.CreateHostContext())
        {
            subdomain = await db.Stores.Where(x => x.Id == c.StoreId).Select(x => x.SubDomainNormalized).SingleAsync();
            before = await Snapshot(db);
        }
        await using (var host = await Web01PublicProcess.StartAsync(fixture.Database.ConnectionString, "shop.web01.test", subdomain))
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/shop?tenant=foreign&storeId=999"); request.Headers.Host = "shop.web01.test";
            using var response = await host.Http.SendAsync(request); Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.True(response.Headers.CacheControl?.NoStore); Assert.Contains("đang chuẩn bị", await response.Content.ReadAsStringAsync());
            using var wrong = new HttpRequestMessage(HttpMethod.Get, "/shop"); wrong.Headers.Host = "foreign.web01.test";
            using var rejected = await host.Http.SendAsync(wrong); Assert.Equal(HttpStatusCode.NotFound, rejected.StatusCode);
            using var admin = new HttpRequestMessage(HttpMethod.Get, "/admin/pos"); admin.Headers.Host = "shop.web01.test";
            using var absent = await host.Http.SendAsync(admin); Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        }
        await using var after = fixture.Database.CreateHostContext(); Assert.Equal(before, await Snapshot(after));
    }

    private static async Task<string> Snapshot(GaoApp.Infrastructure.Data.AppDbContext db) => JsonSerializer.Serialize(new
    {
        history = (await db.Database.GetAppliedMigrationsAsync()).ToArray(), stores = await db.Stores.CountAsync(),
        settings = await db.StorefrontSettings.CountAsync(), publications = await db.StorefrontProducts.CountAsync(),
        orders = await db.StorefrontOrders.CountAsync(), pos = await db.Orders.CountAsync(), shifts = await db.POSShifts.CountAsync(),
        inventory = await db.InventoryTransactions.CountAsync(), payments = await db.OrderPayments.CountAsync(),
        menus = await db.AdminMenuItems.CountAsync(), roles = await db.Roles.CountAsync(), grants = await db.RolePermissions.CountAsync()
    });
}

internal sealed class Web01PublicProcess : IAsyncDisposable
{
    private readonly Process process;
    private readonly ConcurrentQueue<string> output = new();
    internal HttpClient Http { get; }
    private Web01PublicProcess(Process process, Uri address)
    {
        this.process = process; Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = address };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
    }
    internal static async Task<Web01PublicProcess> StartAsync(string connection, string host, string subdomain)
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."));
        var dll = DllPath();
        Assert.True(File.Exists(dll), "Build the separate GaoApp.Storefront.Web host before this test; optional GAOAPP_WEB01_PUBLIC_DLL selects its artifact.");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var runtime = Path.Combine(root, ".artifacts/storefront-web01/tests/public-host", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runtime);
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = runtime, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.GetFullPath(dll)); start.ArgumentList.Add("--urls"); start.ArgumentList.Add($"http://127.0.0.1:{port}");
        start.Environment["ASPNETCORE_ENVIRONMENT"] = "Production"; start.Environment["DOTNET_ENVIRONMENT"] = "Production";
        start.Environment["ConnectionStrings__DefaultConnection"] = connection; start.Environment["StorefrontHost__Host"] = host;
        start.Environment["StorefrontHost__StoreSubdomain"] = subdomain;
        var process = new Process { StartInfo = start };
        var fixture = new Web01PublicProcess(process, new Uri($"http://127.0.0.1:{port}"));
        try
        {
            Assert.True(process.Start()); process.BeginOutputReadLine(); process.BeginErrorReadLine();
            var timer = Stopwatch.StartNew();
            while (timer.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (process.HasExited) throw new InvalidOperationException("Public host exited: " + string.Join('\n', fixture.output));
                try { using var response = await fixture.Http.GetAsync("/health"); if (response.IsSuccessStatusCode) return fixture; }
                catch (HttpRequestException) { }
                await Task.Delay(150);
            }
            throw new TimeoutException("Public host did not become healthy: " + string.Join('\n', fixture.output));
        }
        catch { await fixture.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        Http.Dispose();
        if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        process.Dispose();
    }
    internal static string DllPath()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath())!, "../.."));
        return Environment.GetEnvironmentVariable("GAOAPP_WEB01_PUBLIC_DLL") ?? Path.Combine(root, "GaoApp.Storefront.Web/bin/Debug/net10.0/GaoApp.Storefront.Web.dll");
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;
}
