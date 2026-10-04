using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Identity;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Inventory;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

/// <summary>Real Web entry point, middleware, controllers, services and SQL; no fake identity or business handler.</summary>
internal sealed class FullApplicationFixture : IAsyncDisposable
{
    internal InventoryPostingLocalDb Database { get; } = new();
    internal sealed record StoreSeed(int StoreId, int WarehouseId, int VariantId, int TerminalId, string Host);
    internal sealed record Account(StoreSeed Store, int UserId, int RoleId, string Name, string Password);
    internal List<StoreSeed> Stores { get; } = [];
    internal Uri Address { get; private set; } = null!;
    private Process? process;
    private string runtimeRoot = "";
    private readonly ConcurrentQueue<string> output = new();
    internal static string SourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GaoApp.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Workspace root not found.");
    }

    internal static Task<FullApplicationFixture> StartAsync() => StartWithSecurityAsync(initializeSecurity: true);
    internal static Task<FullApplicationFixture> StartWithInvoiceLibraryAsync() => StartConfiguredAsync(true, true);
    internal string InvoiceLibraryRoot => Path.Combine(runtimeRoot, "uploads", "XML");

    internal static Task<FullApplicationFixture> StartWithSecurityAsync(bool initializeSecurity)
        => StartConfiguredAsync(initializeSecurity, false);

    private static async Task<FullApplicationFixture> StartConfiguredAsync(bool initializeSecurity, bool enableInvoiceLibrary)
    {
        var fixture = new FullApplicationFixture();
        try
        {
            await fixture.Database.MigrateAsync();
            for (var i = 0; i < 2; i++)
            {
                var catalog = await fixture.Database.SeedInventoryCatalogAsync();
                await using var db = fixture.Database.CreateTenantContext(catalog.StoreId);
                var store = await db.Stores.SingleAsync(x => x.Id == catalog.StoreId);
                var terminal = new POSTerminal { StoreId = store.Id, Code = "E2E-01", Name = "E2E terminal" };
                db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
                await InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                    new GaoApp.Application.Services.Inventory.InventoryMovementFactory().CreatePurchaseReceipt(
                        catalog.WarehouseId, catalog.ProductVariantId, 100, 10, "E2E-INITIAL", 1, "E2E test stock", 1));
                fixture.Stores.Add(new(store.Id, catalog.WarehouseId, catalog.ProductVariantId, terminal.Id, store.SubDomain + ".localhost"));
            }
            if (initializeSecurity)
            {
                await using var seedDb = fixture.Database.CreateHostContext();
                // Preserve the fixture's former Development dataset, now owned explicitly by the test.
                await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedPermissionsAsync(seedDb);
                await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedLegalEntityAdminMenusAsync(seedDb);
                await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedPurchaseAdminMenusAsync(seedDb);
                await GaoApp.Infrastructure.Data.Seed.ProductLabelMenuSeeder.SeedAsync(seedDb);
                await GaoApp.Infrastructure.Data.Seed.SecuritySeedData.SeedDefaultRolesForAllStoresAsync(seedDb);
            }
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            fixture.Address = new Uri($"http://127.0.0.1:{port}");
            fixture.runtimeRoot = Path.Combine(SourceRoot(), "Logs", "security-phase4-runtime", Guid.NewGuid().ToString("N"));
            var contentRoot = Path.Combine(fixture.runtimeRoot, "app"); Directory.CreateDirectory(contentRoot);
            var settings = new
            {
                ConnectionStrings = new { DefaultConnection = fixture.Database.ConnectionString },
                AppUrl = new { BaseUrl = fixture.Address.ToString(), AdminUrl = $"http://admin.localhost:{port}" },
                Tenant = new { RootDomain = "localhost", AdminSubdomain = "admin" },
                AllowedHosts = "127.0.0.1;localhost;*.localhost",
                Storage = new { UploadRoot = Path.Combine(fixture.runtimeRoot, "uploads"), CreateIfMissing = true },
                InputInvoiceLibrary = new { Enabled = enableInvoiceLibrary, RootPath = "XML" },
                DataProtection = new { KeysPath = Path.Combine(fixture.runtimeRoot, "keys") },
                SeedData = new { EnableDemoSeed = false, EnableDefaultAdminSeed = false },
                TaxCodeLookup = new { Enabled = false },
                Proxy = new { EnableForwardedHeaders = false },
                Serilog = new { MinimumLevel = new { Default = "Warning" }, WriteTo = new[] { new { Name = "Console" } } }
            };
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "appsettings.json"), JsonSerializer.Serialize(settings));
            // Use the Web build paired with this test artifact, not the running app or a default output folder.
            var webDll = TestApplicationBuild.WebAssemblyPath();
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = contentRoot, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var configurationPrefixes = new[] { "ConnectionStrings", "AppUrl", "Tenant", "Storage", "InputInvoiceLibrary", "DataProtection", "SeedData", "ProductionBootstrap", "Serilog", "Proxy", "Kestrel", "TaxCodeLookup", "AcbCallbackRouting" };
            foreach (var key in start.Environment.Keys.ToArray())
                if (configurationPrefixes.Any(prefix => key.StartsWith(prefix + "__", StringComparison.OrdinalIgnoreCase) || key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)))
                    start.Environment.Remove(key);
            start.ArgumentList.Add(webDll); start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(contentRoot);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = fixture.Address.ToString();
            start.Environment["ConnectionStrings__DefaultConnection"] = fixture.Database.ConnectionString;
            start.Environment["SeedData__EnableDemoSeed"] = "false";
            start.Environment["SeedData__EnableDefaultAdminSeed"] = "false";
            start.Environment["TaxCodeLookup__Enabled"] = "false";
            fixture.process = new Process { StartInfo = start };
            fixture.process.OutputDataReceived += (_, e) => { if (e.Data is not null) fixture.output.Enqueue(e.Data); };
            fixture.process.ErrorDataReceived += (_, e) => { if (e.Data is not null) fixture.output.Enqueue(e.Data); };
            fixture.process.Start(); fixture.process.BeginOutputReadLine(); fixture.process.BeginErrorReadLine();
            using var probe = new HttpClient { BaseAddress = fixture.Address, Timeout = TimeSpan.FromSeconds(2) };
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed < TimeSpan.FromSeconds(60))
            {
                if (fixture.process.HasExited) throw new InvalidOperationException("Test Web failed to start: " + string.Join('\n', fixture.output.TakeLast(18)));
                try { using var response = await probe.GetAsync("/health/live"); if (response.IsSuccessStatusCode) return fixture; }
                catch (HttpRequestException) { }
                catch (TaskCanceledException) { }
                await Task.Delay(150);
            }
            throw new TimeoutException("Test Web startup timed out: " + string.Join('\n', fixture.output.TakeLast(15)));
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal async Task<Account> AddAccountAsync(StoreSeed store, params string[] permissions)
    {
        await using var db = Database.CreateTenantContext(store.StoreId);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var password = "E2e-" + Guid.NewGuid().ToString("N") + "!";
        var user = new User { UserName = "e2e_" + suffix, PasswordHash = new PasswordHasher().Hash(password), FullName = "E2E test user" };
        var role = new Role { StoreId = store.StoreId, Code = "E2E_" + suffix, Name = "E2E role" };
        db.UserInStores.Add(new UserInStore { StoreId = store.StoreId, User = user, Role = role });
        var requested = permissions.Contains("*") ? PermissionCatalog.All.Select(x => x.Code).ToArray() : permissions;
        var grants = await db.Permissions.Where(x => requested.Contains(x.Code)).ToListAsync();
        Assert.Equal(requested.Distinct().Count(), grants.Count);
        foreach (var permission in grants) db.RolePermissions.Add(new RolePermission { Role = role, PermissionId = permission.Id });
        await db.SaveChangesAsync();
        return new(store, user.Id, role.Id, user.UserName, password);
    }

    internal Client Anonymous(StoreSeed store) => new(Address, store.Host);
    internal async Task<Client> LoginAsync(Account account)
    {
        var client = Anonymous(account.Store);
        try
        {
            var html = await client.Http.GetStringAsync("/admin/account/login");
            var token = AntiforgeryToken(html);
            using var response = await client.Http.PostAsync("/admin/account/login", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["UserName"] = account.Name, ["Password"] = account.Password,
                ["SelectedTerminalId"] = account.Store.TerminalId.ToString(), ["__RequestVerificationToken"] = token }));
            Assert.True(response.StatusCode == HttpStatusCode.Redirect, $"Login failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            // Antiforgery token must be refreshed after the principal changes.
            html = await client.Http.GetStringAsync("/admin/account/login");
            client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", AntiforgeryToken(html));
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static string AntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Missing antiforgery token in the real login page.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    internal sealed class Client : IDisposable
    {
        public CookieContainer Cookies { get; } = new();
        public HttpClient Http { get; }
        public string Host { get; }
        public Client(Uri address, string host)
        {
            Host = host;
            Http = new(new HttpClientHandler { CookieContainer = Cookies, AllowAutoRedirect = false })
                { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };
            Http.DefaultRequestHeaders.Host = host;
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }
        public async Task<JsonElement> JsonAsync(HttpMethod method, string path, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            using var response = await Http.SendAsync(request);
            var text = await response.Content.ReadAsStringAsync();
            Assert.True(response.IsSuccessStatusCode, $"{method} {path}: {(int)response.StatusCode} {text}");
            return JsonDocument.Parse(text).RootElement.Clone();
        }
        public void Dispose() => Http.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (process is not null)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            process.Dispose(); process = null;
        }
        var evidence = Path.Combine(SourceRoot(), "TestResults", "security-phase4"); Directory.CreateDirectory(evidence);
        await File.WriteAllLinesAsync(Path.Combine(evidence, "web-test-" + Path.GetFileName(runtimeRoot) + ".log"), output);
        await Database.DisposeAsync();
        if (runtimeRoot.Length > 0)
        {
            var allowed = Path.GetFullPath(Path.Combine(SourceRoot(), "Logs", "security-phase4-runtime")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(runtimeRoot);
            if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) throw new InvalidOperationException("Unsafe test cleanup target.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
