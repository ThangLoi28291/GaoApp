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
internal sealed class Ui20CurrentModelApplicationFixture : IAsyncDisposable
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

    internal static Task<Ui20CurrentModelApplicationFixture> StartAsync() => StartWithSecurityAsync(initializeSecurity: true);
    internal static Task<Ui20CurrentModelApplicationFixture> StartPurchasingAsync() => StartConfiguredAsync(true, false, enablePurchaseReceiving: true);
    internal static Task<Ui20CurrentModelApplicationFixture> StartWithAuditPausedAsync() => StartConfiguredAsync(true, false, enableAuditOutbox: false);
    internal static Task<Ui20CurrentModelApplicationFixture> StartInvoicePrivacyAsync() => StartConfiguredAsync(true, false, invoicePrivacyLogs: true);
    internal string LogOutput => string.Join('\n', output);
    // Test-only trusted loopback proxy for the phone API's HTTPS requirement.
    // Browser requests stay HTTP; only the test phone client forwards its scheme.
    internal static Task<Ui20CurrentModelApplicationFixture> StartBankNotificationAsync()
        => StartConfiguredAsync(true, false, trustLoopbackProxy: true);
    internal static Task<Ui20CurrentModelApplicationFixture> StartDeliveryAsync()
        => StartConfiguredAsync(true, false, enableDeliveryOutbox: false);
    internal static Task<Ui20CurrentModelApplicationFixture> StartWithInvoiceLibraryAsync() => StartConfiguredAsync(true, true);
    internal string InvoiceLibraryRoot => Path.Combine(runtimeRoot, "uploads", "XML");
    internal string CustomerServiceDatabasePath => Path.Combine(runtimeRoot,"customer-service.db");

    internal static Task<Ui20CurrentModelApplicationFixture> StartWithSecurityAsync(bool initializeSecurity)
        => StartConfiguredAsync(initializeSecurity, false);

    private static async Task<Ui20CurrentModelApplicationFixture> StartConfiguredAsync(bool initializeSecurity, bool enableInvoiceLibrary, bool enableDeliveryOutbox = true, bool trustLoopbackProxy = false, bool invoicePrivacyLogs = false, bool enableAuditOutbox = true, bool enablePurchaseReceiving = false)
    {
        var fixture = new Ui20CurrentModelApplicationFixture();
        try
        {
            // UI-24 revalidates the original UI-20 flows through the real migration chain.
            await fixture.Database.MigrateAsync();
            await using (var schema = fixture.Database.CreateHostContext())
            {
                Assert.False(schema.Database.HasPendingModelChanges());
                Assert.Empty(await schema.Database.GetPendingMigrationsAsync());
                Assert.NotEmpty(await schema.Database.GetAppliedMigrationsAsync());
            }
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
            fixture.runtimeRoot = Path.Combine(SourceRoot(), "TestResults", "mobile-ui-20-20261028", "runtime", Guid.NewGuid().ToString("N"));
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
                CustomerService = new { DatabasePath = Path.Combine(fixture.runtimeRoot, "customer-service.db") },
                CustomerChannels = new { Enabled = false },
                SeedData = new { EnableDemoSeed = false, EnableDefaultAdminSeed = false },
                TaxCodeLookup = new { Enabled = false },
                DeliveryOutbox = new { Enabled = enableDeliveryOutbox },
                AuditOutbox = new { Enabled = enableAuditOutbox },
                ReceivingWorkbench = new { Enabled = enablePurchaseReceiving },
                Proxy = new { EnableForwardedHeaders = trustLoopbackProxy, KnownProxies = trustLoopbackProxy ? new[] { "127.0.0.1", "::1" } : Array.Empty<string>() },
                Serilog = new { MinimumLevel = new { Default = invoicePrivacyLogs ? "Information" : "Warning" }, WriteTo = new[] { new { Name = "Console" } } }
            };
            await File.WriteAllTextAsync(Path.Combine(contentRoot, "appsettings.json"), JsonSerializer.Serialize(settings));
            // Use the Web build paired with this test artifact, not the running app or a default output folder.
            var webDll = TestApplicationBuild.WebAssemblyPath();
            var start = new ProcessStartInfo("dotnet") { WorkingDirectory = contentRoot, UseShellExecute = false,
                CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var configurationPrefixes = new[] { "ConnectionStrings", "AppUrl", "Tenant", "Storage", "InputInvoiceLibrary", "DataProtection", "SeedData", "ProductionBootstrap", "Serilog", "Proxy", "Kestrel", "TaxCodeLookup", "AcbCallbackRouting", "DeliveryOutbox", "Delivery", "AuditOutbox", "Security", "CustomerService", "CustomerChannels", "Assistant" };
            foreach (var key in start.Environment.Keys.ToArray())
                if (configurationPrefixes.Any(prefix => key.StartsWith(prefix + "__", StringComparison.OrdinalIgnoreCase) || key.StartsWith(prefix + ":", StringComparison.OrdinalIgnoreCase)))
                    start.Environment.Remove(key);
            start.ArgumentList.Add(webDll); start.ArgumentList.Add("--contentRoot"); start.ArgumentList.Add(contentRoot);
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_URLS"] = fixture.Address.ToString();
            start.Environment["ConnectionStrings__DefaultConnection"] = fixture.Database.ConnectionString;
            // The child process must not load a developer's real Development user-secrets or storage.
            start.Environment["APPDATA"] = Path.Combine(fixture.runtimeRoot, "isolated-appdata");
            start.Environment["LOCALAPPDATA"] = Path.Combine(fixture.runtimeRoot, "isolated-localappdata");
            start.Environment["Assistant__Enabled"] = "false";
            start.Environment["Assistant__StoreSettingsDirectory"] = Path.Combine(fixture.runtimeRoot, "assistant-settings");
            start.Environment["CustomerChannels__Enabled"] = "false";
            start.Environment["CustomerChannels__WorkerEnabled"] = "false";
            start.Environment["Storage__UploadRoot"] = Path.Combine(fixture.runtimeRoot, "uploads");
            start.Environment["Storage__CreateIfMissing"] = "true";
            start.Environment["CustomerService__DatabasePath"] = Path.Combine(fixture.runtimeRoot, "customer-service.db");
            start.Environment["DataProtection__KeysPath"] = Path.Combine(fixture.runtimeRoot, "keys");
            start.Environment["Database__ApplyMigrationsOnStartup"] = "false";
            start.Environment["GaoAppDevBrowser__Enabled"] = "false";
            start.Environment["MediaCleanup__Enabled"] = "false";
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
        return await AddAccountCoreAsync(store, permissions);
    }

    internal async Task ConfigureResourceProtectionAsync(IReadOnlyDictionary<string, string> settings)
    {
        var start = process?.StartInfo ?? throw new InvalidOperationException("Fixture Web not started.");
        foreach (var setting in settings) start.Environment["Security__ResourceProtection__" + setting.Key] = setting.Value;
        await RestartAsync(enableDeliveryOutbox: false);
    }

    internal async Task ConfigureCustomerChannelsAsync(IReadOnlyDictionary<string,string> settings)
    {
        var start=process?.StartInfo??throw new InvalidOperationException("Fixture Web not started.");
        start.Environment["CustomerChannels__Enabled"]="true";
        start.Environment["CustomerChannels__WorkerEnabled"]="false";
        if(!settings.ContainsKey("CredentialDirectory")) {
            var token=settings.FirstOrDefault(p=>p.Key.EndsWith("__AccessTokenFile",StringComparison.Ordinal)).Value;
            if(!string.IsNullOrWhiteSpace(token))start.Environment["CustomerChannels__CredentialDirectory"]=Path.GetDirectoryName(token)!;
        }
        foreach(var setting in settings)start.Environment["CustomerChannels__"+setting.Key]=setting.Value;
        await RestartAsync(enableDeliveryOutbox:false);
    }
    internal async Task ConfigureCustomerServiceAsync(IReadOnlyDictionary<string,string> settings)
    {
        var start=process?.StartInfo??throw new InvalidOperationException("Fixture Web not started.");
        foreach(var setting in settings)start.Environment["CustomerService__"+setting.Key]=setting.Value;
        await RestartAsync(enableDeliveryOutbox:false);
    }
    internal async Task ConfigureAssistantAsync(IReadOnlyDictionary<string, string> settings)
    {
        var start = process?.StartInfo ?? throw new InvalidOperationException("Fixture Web not started.");
        foreach (var setting in settings) start.Environment["Assistant__" + setting.Key] = setting.Value;
        await RestartAsync(enableDeliveryOutbox: false);
    }

    internal async Task ConfigureCspAsync(bool reportOnly)
    {
        var start = process?.StartInfo ?? throw new InvalidOperationException("Fixture Web not started.");
        start.Environment["Security__Csp__ReportOnly"] = reportOnly.ToString();
        await RestartAsync(enableDeliveryOutbox: false);
    }

    // Restart only the Web process owned by this fixture, preserving SQL, address and cookie keys.
    internal async Task RestartAsync(bool enableDeliveryOutbox, bool? enableAuditOutbox = null)
    {
        var previous = process ?? throw new InvalidOperationException("Fixture Web not started.");
        var start = previous.StartInfo;
        if (!previous.HasExited) { previous.Kill(entireProcessTree: true); await previous.WaitForExitAsync(); }
        previous.Dispose();
        start.Environment["DeliveryOutbox__Enabled"] = enableDeliveryOutbox.ToString();
        if (enableAuditOutbox.HasValue) start.Environment["AuditOutbox__Enabled"] = enableAuditOutbox.Value.ToString();
        process = new Process { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.Enqueue(e.Data); };
        process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
        using var probe = new HttpClient { BaseAddress = Address, Timeout = TimeSpan.FromSeconds(2) };
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (process.HasExited) throw new InvalidOperationException("Restarted test Web failed: " + string.Join('\n', output.TakeLast(18)));
            try { using var response = await probe.GetAsync("/health/live"); if (response.IsSuccessStatusCode) return; }
            catch (HttpRequestException) { }
            catch (TaskCanceledException) { }
            await Task.Delay(200);
        }
        throw new TimeoutException("Restarted test Web did not become ready.");
    }

    private async Task<Account> AddAccountCoreAsync(StoreSeed store, string[] permissions)
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
        var evidence = Path.Combine(SourceRoot(), "TestResults", "mobile-ui-20-20261028", "server-logs"); Directory.CreateDirectory(evidence);
        await File.WriteAllLinesAsync(Path.Combine(evidence, "web-test-" + Path.GetFileName(runtimeRoot) + ".log"), output);
        await Database.DisposeAsync();
        if (runtimeRoot.Length > 0)
        {
            var allowed = Path.GetFullPath(Path.Combine(SourceRoot(), "TestResults", "mobile-ui-20-20261028", "runtime")) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(runtimeRoot);
            if (!target.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) throw new InvalidOperationException("Unsafe test cleanup target.");
            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        }
    }
}
