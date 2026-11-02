using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PublicPagesMobileBrowserTests
{
    [Fact]
    public async Task Existing_invoice_browser_probe_keeps_expiry_back_navigation_and_replacement()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var account = await app.AddAccountAsync(app.Stores[0], "*");
        using var staff = await app.LoginAsync(account);
        var orderId = await InvoiceBuyerSelfServiceSecurityTests.Seed(app, account);
        var data = await staff.JsonAsync(HttpMethod.Get, $"/admin/pos/receipt/{orderId}?data=true");
        var root = FullApplicationFixture.SourceRoot();
        var output = MobileAcceptanceEvidence.PathFor(root, "TestResults", "mobile-ui-23-20261028", "invoice-regression"); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "invoice-buyer-self-service.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{account.Store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
            terminalId = account.Store.TerminalId, orderId,
            link = new Uri(data.GetProperty("invoiceBuyerSelfServiceUrl").GetString()!).PathAndQuery, evidenceRoot = output
        }));
        process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(4)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), log);
        Assert.True(process.ExitCode == 0, log);
    }

    [Fact]
    public async Task Native_public_phone_pages_preserve_forms_tokens_scope_and_desktop()
    {
        await using var app = await FullApplicationFixture.StartWithAuditPausedAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var staff = await app.LoginAsync(account);
        async Task<int> Seed(string number)
        {
            var id = await InvoiceBuyerSelfServiceSecurityTests.Seed(app, account);
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            (await db.Orders.SingleAsync(x => x.Id == id)).OrderNumber = number;
            await db.SaveChangesAsync();
            return id;
        }
        var active = await Seed("UI23-ACTIVE");
        var expired = await Seed("UI23-EXPIRED");
        var locked = await Seed("UI23-LOCKED");
        async Task<string> Link(int id)
        {
            var data = await staff.JsonAsync(HttpMethod.Get, $"/admin/pos/receipt/{id}?data=true");
            return new Uri(data.GetProperty("invoiceBuyerSelfServiceUrl").GetString()!).PathAndQuery;
        }
        var activeLink = await Link(active); var expiredLink = await Link(expired); var lockedLink = await Link(locked);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Orders.SingleAsync(x => x.Id == expired)).CompletedAtUtc = DateTime.UtcNow.AddHours(-3);
            (await db.InvoiceHeads.SingleAsync(x => x.OrderId == locked)).IsLocked = true;
            await db.SaveChangesAsync();
        }
        var clock = new AssistantIntegrationTests.FixedClock { Now = DateTimeOffset.UtcNow };
        var inbox = new CustomerServiceStore(new AssistantIntegrationTests.EnvironmentStub(Path.Combine(Path.GetDirectoryName(app.CustomerServiceDatabasePath)!, "app")),
            new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new() { DatabasePath = app.CustomerServiceDatabasePath }), clock);
        inbox.Configure(store.StoreId, true, inbox.Settings(store.StoreId).Version);
        // All configuration writes above belong to this disposable GUID fixture, before the invariant snapshot.
        async Task<string> StableState()
        {
            await using var db = app.Database.CreateHostContext();
            return JsonSerializer.Serialize(new
            {
                menus = await db.AdminMenuItems.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                stores = await db.Stores.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
                grants = await db.RolePermissions.AsNoTracking().OrderBy(x => x.RoleId).ThenBy(x => x.PermissionId).ToListAsync(),
                orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.GrandTotal }).ToListAsync()
            });
        }
        var before = await StableState();
        var root = FullApplicationFixture.SourceRoot();
        var output = MobileAcceptanceEvidence.PathFor(root, "TestResults", "mobile-ui-23-20261028", "browser"); Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "public-pages-mobile.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new
        {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", disabledUrl = $"http://{app.Stores[1].Host}:{app.Address.Port}",
            activeLink, expiredLink, lockedLink, output, account.Name, account.Password, terminalId = store.TerminalId
        }));
        process.StandardInput.Close();
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(14)); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
        var log = await stdout + "\n" + await stderr; await File.WriteAllTextAsync(Path.Combine(output, "browser.log"), log);
        Assert.True(process.ExitCode == 0, log);
        Assert.Equal(before, await StableState());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.False(db.Database.HasPendingModelChanges());
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.Equal("UI23 khách đã cập nhật", (await db.InvoiceHeads.SingleAsync(x => x.OrderId == active)).BuyerName);
            Assert.Equal("PRIVATE-BUYER-ISSUE4", (await db.InvoiceHeads.SingleAsync(x => x.OrderId == expired)).BuyerName);
            Assert.Equal("PRIVATE-BUYER-ISSUE4", (await db.InvoiceHeads.SingleAsync(x => x.OrderId == locked)).BuyerName);
        }
        await File.WriteAllTextAsync(Path.Combine(output, "sql-checks.json"), JsonSerializer.Serialize(new
        {
            passed = true, schema = "MigrateAsync", pendingMigrations = 0, pendingModelChanges = false,
            menuStoreGrantsOrderAmountsUnchanged = true, activeInvoiceUpdated = true, expiredAndLockedInvoiceUntouched = true,
            beforeSha256 = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(before))),
            existingChromeWindowsUntouched = true
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
