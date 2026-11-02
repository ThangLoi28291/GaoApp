using System.Diagnostics;
using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosOfflinePrivacySqlServerTests
{
    [Fact]
    public async Task Offline_customers_include_only_unfinished_orders_of_the_current_shift_and_exclude_addresses()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        int draftCustomer, heldCustomer;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync();
            var customers = Enumerable.Range(0, 260).Select(i => new Customer { StoreId = store.StoreId,
                Name = "Privacy customer " + i, Phone = "090" + i.ToString("D7"), Address = "PRIVATE-ADDRESS-" + i,
                PriceTier = i == 0 ? "WHOLESALE" : "RETAIL", AskBeforePrintingReceipt = true }).ToArray();
            db.Customers.AddRange(customers); await db.SaveChangesAsync();
            draftCustomer = customers[0].Id; heldCustomer = customers[1].Id;
            db.Orders.AddRange(
                new Order { StoreId = store.StoreId, POSShiftId = shift.Id, CustomerId = draftCustomer, Status = OrderStatus.Draft },
                new Order { StoreId = store.StoreId, POSShiftId = shift.Id, CustomerId = heldCustomer, Status = OrderStatus.OnHold },
                new Order { StoreId = store.StoreId, POSShiftId = shift.Id, CustomerId = customers[2].Id, Status = OrderStatus.Completed },
                new Order { StoreId = store.StoreId, POSShiftId = shift.Id, CustomerId = customers[3].Id, Status = OrderStatus.Cancelled },
                new Order { StoreId = store.StoreId, POSShiftId = shift.Id, CustomerId = customers[4].Id, IsDeleted = true });
            var oldShift = new POSShift { StoreId = store.StoreId, TerminalId = store.TerminalId,
                WarehouseId = store.WarehouseId, OpenedByUserId = account.UserId, Status = POSShiftStatus.Closed, ShiftCode = "PRIVACY-OLD" };
            db.POSShifts.Add(oldShift); await db.SaveChangesAsync();
            db.Orders.Add(new Order { StoreId = store.StoreId, POSShiftId = oldShift.Id, CustomerId = customers[5].Id });
            await db.SaveChangesAsync();
            // The audit interceptor initializes newly added rows as not deleted.
            var deleted = await db.Orders.SingleAsync(x => x.CustomerId == customers[4].Id);
            deleted.IsDeleted = true; await db.SaveChangesAsync();
        }
        await using (var db = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            db.Customers.Add(new Customer { StoreId = app.Stores[1].StoreId, Name = "Privacy foreign customer", Phone = "0999999999" });
            await db.SaveChangesAsync();
        }
        var page = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/customers");
        Assert.Equal(new[] { draftCustomer, heldCustomer }, page.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("customerId").GetInt32()));
        Assert.Equal(JsonValueKind.Null, page.GetProperty("nextAfterId").ValueKind);
        Assert.DoesNotContain("address", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("foreign", page.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal("WHOLESALE", page.GetProperty("items")[0].GetProperty("priceTier").GetString());
        var next = await client.JsonAsync(HttpMethod.Get, $"/admin/pos/offline/customers?afterId={draftCustomer}");
        Assert.Single(next.GetProperty("items").EnumerateArray());
        var online = await client.JsonAsync(HttpMethod.Get, "/admin/pos/customers/search?keyword=0900000259");
        Assert.Contains("Privacy customer 259", online.GetRawText());
        var bootstrap = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/bootstrap");
        var status = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/status");
        Assert.Equal(bootstrap.GetProperty("deviceBinding").GetString(), status.GetProperty("deviceBinding").GetString());
        Assert.Equal(64, bootstrap.GetProperty("deviceBinding").GetString()!.Length);
        Assert.DoesNotContain(client.Cookies.GetCookies(app.Address)[PosDeviceSession.CookieName]!.Value, bootstrap.GetRawText());
        using var otherDevice = await app.LoginAsync(account);
        var other = await otherDevice.JsonAsync(HttpMethod.Get, "/admin/pos/offline/status");
        Assert.NotEqual(status.GetProperty("deviceBinding").GetString(), other.GetProperty("deviceBinding").GetString());
        using var anonymous = app.Anonymous(store);
        using var denied = await anonymous.Http.GetAsync("/admin/pos/offline/customers");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Chrome_preserves_pending_sales_through_logout_expiry_and_verified_reauthentication()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var selected = await client.JsonAsync(HttpMethod.Post, "/admin/pos/customers/quick-create", new {
            name = "Attached privacy customer", phone = "0900000001", address = "DO-NOT-CACHE-ADDRESS", priceTier = "WHOLESALE" });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.Customers.Add(new Customer { StoreId = store.StoreId, Name = "Unrelated privacy customer", Phone = "0900000002", Address = "UNRELATED-ADDRESS" });
            await db.SaveChangesAsync();
        }
        var root = FullApplicationFixture.SourceRoot();
        var output = Path.Combine(root, ".artifacts", "security-issue7", "browser");
        Directory.CreateDirectory(output);
        var start = new ProcessStartInfo("node") { WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["NODE_PATH"] = Path.Combine(root, "Logs", "pos-offline-browser-deps", "node_modules");
        start.ArgumentList.Add(Path.Combine(root, "GaoApp.Tests.Browser", "pos-offline-privacy.browser.cjs"));
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new {
            baseUrl = $"http://{store.Host}:{app.Address.Port}", user = account.Name, password = account.Password,
            terminalId = store.TerminalId, variantId = store.VariantId, output
        }));
        process.StandardInput.Close();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(6));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(true); throw; }
        var result = await stdout + "\n" + await stderr;
        await File.WriteAllTextAsync(Path.Combine(output, "chrome.log"), result);
        Assert.True(process.ExitCode == 0, result);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await final.OrderPayments.ToListAsync());
        Assert.Single(await final.Orders.Where(x => x.Status == OrderStatus.Completed).ToListAsync());
        Assert.Equal(98, (await final.InventoryBalances.SingleAsync()).OnHandQty);
    }
}
