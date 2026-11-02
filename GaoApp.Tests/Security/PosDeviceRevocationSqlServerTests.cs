using System.Net;
using System.Net.WebSockets;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosDeviceRevocationSqlServerTests
{
    private const string Status = "/admin/pos/customer-display/payment-state";

    [Fact]
    public async Task Revoke_one_device_closes_its_HTTP_and_socket_sessions_and_preserves_same_user_same_terminal_and_business_data()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var first = await app.LoginAsync(account);
        using var second = await app.LoginAsync(account);
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var firstKey = Key(first);
        var secondKey = Key(second);
        Assert.NotEqual(firstKey, secondKey);
        using var originalTicket = Snapshot(app, store, first);
        using var websocketReplay = Snapshot(app, store, first);
        using var swappedKeyTicket = Snapshot(app, store, first);
        swappedKeyTicket.Cookies.Add(app.Address, new Cookie(PosDeviceSession.CookieName, secondKey));
        using var authOnlyTicket = Snapshot(app, store, first, includeDevice: false);
        await using var firstSocket = await PosWebSocketClient.ConnectAsync(first, store.StoreId, store.TerminalId);
        await using var secondSocket = await PosWebSocketClient.ConnectAsync(second, store.StoreId, store.TerminalId);
        await second.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var order = await second.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var orderId = order.GetProperty("orderId").GetInt32();
        await second.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=2");
        int deviceId;
        string before;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            deviceId = (await db.POSTerminalDevices.SingleAsync(x => x.DeviceKey == firstKey)).Id;
            before = await BusinessState(db);
        }
        using (var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1])))
        using (var denied = await foreign.Http.PostAsync($"/admin/pos-terminals/keys/{deviceId}/revoke", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await IsActive(first);
        using (var denied = await second.Http.PostAsync($"/admin/pos-terminals/keys/{deviceId}/revoke", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await secondSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_refresh", payload = new { } });
        await firstSocket.WaitForEventAsync("customer_payment_display_changed");
        await secondSocket.WaitForEventAsync("customer_payment_display_changed");
        firstSocket.Events.Clear(); secondSocket.Events.Clear();
        using (var result = await admin.Http.PostAsync($"/admin/pos-terminals/keys/{deviceId}/revoke", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        await firstSocket.WaitForCloseAsync();
        await IsRejected(first);
        await IsRejected(swappedKeyTicket);
        await IsRejected(authOnlyTicket);
        await IsRejected(originalTicket);
        await Assert.ThrowsAnyAsync<WebSocketException>(async () =>
        { await using var socket = await PosWebSocketClient.ConnectAsync(websocketReplay, store.StoreId, store.TerminalId); });
        await secondSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_refresh", payload = new { } });
        await secondSocket.WaitForEventAsync("customer_payment_display_changed");
        Assert.False(firstSocket.HasEvent("customer_payment_display_changed"));
        await IsActive(second);
        await IsActive(admin);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.False((await db.POSTerminalDevices.SingleAsync(x => x.Id == deviceId)).IsActive);
            Assert.True((await db.POSTerminalDevices.SingleAsync(x => x.DeviceKey == secondKey)).IsActive);
            Assert.Equal(before, await BusinessState(db));
        }
        // Normal account/password + terminal selection remains available after the old device is revoked.
        using var newLogin = await app.LoginAsync(account);
        Assert.NotEqual(firstKey, Key(newLogin));
        await IsActive(newLogin);
        await IsActive(second);
    }

    [Fact]
    public async Task SQL_disable_then_reenable_does_not_revive_old_tickets_or_idle_sockets_and_new_login_still_works()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var first = await app.LoginAsync(account);
        using var second = await app.LoginAsync(account);
        using var oldReplay = Snapshot(app, store, first);
        var key = Key(first);
        await using var firstSocket = await PosWebSocketClient.ConnectAsync(first, store.StoreId, store.TerminalId);
        await using var secondSocket = await PosWebSocketClient.ConnectAsync(second, store.StoreId, store.TerminalId);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var device = await db.POSTerminalDevices.SingleAsync(x => x.DeviceKey == key);
            device.IsActive = false; await db.SaveChangesAsync();
            device.IsActive = true; await db.SaveChangesAsync();
        }
        await firstSocket.WaitForCloseAsync(); // Existing idle-connection worker also checks device generations.
        await IsRejected(first);
        await IsRejected(oldReplay);
        await IsActive(second);
        using var fresh = app.Anonymous(store);
        fresh.Cookies.Add(app.Address, new Cookie(PosDeviceSession.CookieName, key));
        await LoginExistingDevice(fresh, account);
        await IsActive(fresh);
        await using var newSocket = await PosWebSocketClient.ConnectAsync(fresh, store.StoreId, store.TerminalId);
        await secondSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_refresh", payload = new { } });
        await newSocket.WaitForEventAsync("customer_payment_display_changed");
    }

    [Fact]
    public async Task Logging_out_one_device_keeps_another_device_of_the_same_employee_and_terminal_connected()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var first = await app.LoginAsync(account);
        using var second = await app.LoginAsync(account);
        await using var firstSocket = await PosWebSocketClient.ConnectAsync(first, store.StoreId, store.TerminalId);
        await using var secondSocket = await PosWebSocketClient.ConnectAsync(second, store.StoreId, store.TerminalId);
        using (var result = await first.Http.PostAsync("/admin/account/logout", null)) Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        await firstSocket.WaitForCloseAsync();
        await IsActive(second);
        await secondSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_refresh", payload = new { } });
        await secondSocket.WaitForEventAsync("customer_payment_display_changed");
    }

    private static string Key(FullApplicationFixture.Client client) => client.Cookies.GetCookies(client.Http.BaseAddress!)[PosDeviceSession.CookieName]!.Value;

    [Fact]
    public async Task Store_monitor_SSE_and_snapshot_recheck_device_revocation_without_blocking_the_other_device()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var first = await app.LoginAsync(account);
        using var second = await app.LoginAsync(account);
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        const string snapshot = "/admin/api/store-activity/snapshot";
        const string stream = "/admin/api/store-activity/stream";
        await first.JsonAsync(HttpMethod.Get, snapshot);
        await second.JsonAsync(HttpMethod.Get, snapshot);
        using var firstStream = await first.Http.GetAsync(stream, HttpCompletionOption.ResponseHeadersRead);
        using var secondStream = await second.Http.GetAsync(stream, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, firstStream.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondStream.StatusCode);
        using var firstReader = new StreamReader(await firstStream.Content.ReadAsStreamAsync());
        using var secondReader = new StreamReader(await secondStream.Content.ReadAsStreamAsync());
        Assert.Equal("snapshot", await NextEvent(firstReader));
        Assert.Equal("snapshot", await NextEvent(secondReader));
        int id;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var key = Key(first);
            id = (await db.POSTerminalDevices.SingleAsync(x => x.DeviceKey == key)).Id;
        }
        using (var revoked = await admin.Http.PostAsync($"/admin/pos-terminals/keys/{id}/revoke", new FormUrlEncodedContent([])))
            Assert.Equal(HttpStatusCode.Redirect, revoked.StatusCode);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        string? next;
        do { next = await NextEvent(firstReader, deadline.Token); } while (next is not null and not "revoked");
        Assert.Equal("revoked", next);
        Assert.Contains(await NextEvent(secondReader), new[] { "snapshot", "heartbeat" });
        using (var rejected = await first.Http.GetAsync(snapshot)) Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        await second.JsonAsync(HttpMethod.Get, snapshot);
    }

    private static async Task<string?> NextEvent(StreamReader reader, CancellationToken ct = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(12));
        while (await reader.ReadLineAsync(deadline.Token) is { } line)
            if (line.StartsWith("event: ", StringComparison.Ordinal)) return line[7..];
        return null;
    }
    private static FullApplicationFixture.Client Snapshot(FullApplicationFixture app, FullApplicationFixture.StoreSeed store,
        FullApplicationFixture.Client original, bool includeDevice = true)
    {
        var client = app.Anonymous(store);
        foreach (Cookie cookie in original.Cookies.GetCookies(app.Address))
            if (includeDevice || cookie.Name != PosDeviceSession.CookieName)
                client.Cookies.Add(app.Address, new Cookie(cookie.Name, cookie.Value, cookie.Path));
        return client;
    }
    private static async Task IsActive(FullApplicationFixture.Client client)
    {
        using var result = await client.Http.GetAsync(Status);
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
    private static async Task IsRejected(FullApplicationFixture.Client client)
    {
        using var result = await client.Http.GetAsync(Status);
        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
    }
    private static async Task LoginExistingDevice(FullApplicationFixture.Client client, FullApplicationFixture.Account account)
    {
        var html = await client.Http.GetStringAsync("/admin/account/login");
        var token = System.Text.RegularExpressions.Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        using var result = await client.Http.PostAsync("/admin/account/login", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["UserName"] = account.Name, ["Password"] = account.Password, ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token) }));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
    }
    private static async Task<string> BusinessState(GaoApp.Infrastructure.Data.AppDbContext db)
        => System.Text.Json.JsonSerializer.Serialize(new
        {
            Orders = await db.Orders.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.GrandTotal, x.PaidTotal, x.BalanceDue }).ToListAsync(),
            Shifts = await db.POSShifts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Status, x.CurrentOrderId }).ToListAsync(),
            Payments = await db.OrderPayments.CountAsync(),
            Inventory = await db.InventoryBalances.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.OnHandQty }).ToListAsync()
        });
}
