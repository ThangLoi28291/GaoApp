using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerPaymentDisplaySecurityTests
{
    private const string StateUrl = "/admin/pos/customer-display/payment-state";

    [Fact]
    public async Task Real_websocket_rejects_forged_success_and_QR_and_never_relays_browser_financial_payloads()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, total) = await Start(app, client, store);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var anonymous = app.Anonymous(store);
        using (var denied = await anonymous.Http.GetAsync(StateUrl)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        await using var socket = await PosWebSocketClient.ConnectAsync(client, store.StoreId, store.TerminalId);
        await using var observer = await PosWebSocketClient.ConnectAsync(viewer, store.StoreId, store.TerminalId);
        await using var other = await PosWebSocketClient.ConnectAsync(foreign, app.Stores[1].StoreId, app.Stores[1].TerminalId);
        foreach (var eventType in new[] { "customer_payment_success", "customer_display_reset", "customer_payment_changed", "order_finalized" })
            await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType, payload = new { finalized = true, paidAmount = total } });
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new { qrDataUrl = "https://attacker.invalid/qr", accountNumber = "ATTACKER" } });
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new { orderId, qrId = int.MaxValue } });
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_preview", payload = new { orderId, amount = -1, method = 0 } });
        await observer.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_refresh", payload = new { } });

        await socket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_preview", payload = new
        {
            orderId, amount = 10, method = 0, balance = 0, paidTotal = total, grandTotal = 1,
            expectedBalance = 0, expectedChange = total, qrDataUrl = "https://attacker.invalid/qr", accountNumber = "ATTACKER"
        } });
        await observer.WaitForEventAsync("customer_payment_display_changed");
        var eventText = string.Join("\n", observer.Events.Select(e => e.GetRawText()));
        Assert.DoesNotContain("ATTACKER", eventText);
        Assert.DoesNotContain("attacker.invalid", eventText);
        Assert.False(other.HasEvent("customer_payment_display_changed"));
        var state = await State(viewer);
        Assert.Equal("preview", state.GetProperty("mode").GetString());
        var preview = state.GetProperty("payload");
        Assert.Equal(total, preview.GetProperty("balance").GetDecimal());
        Assert.Equal(0, preview.GetProperty("paidTotal").GetDecimal());
        Assert.Equal(total - 10, preview.GetProperty("expectedBalance").GetDecimal());
        Assert.Equal(0, preview.GetProperty("expectedChange").GetDecimal());
        Assert.Equal("idle", (await State(foreign)).GetProperty("mode").GetString());
        using var response = await viewer.Http.GetAsync(StateUrl);
        Assert.True(response.Headers.CacheControl?.NoStore);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Empty(await db.OrderPayments.Where(p => p.OrderId == orderId).ToListAsync());
    }

    [Fact]
    public async Task Stored_QR_partial_manual_and_cash_receipts_have_authoritative_totals_and_provenance()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, total) = await Start(app, client, store);
        await using var socket = await PosWebSocketClient.ConnectAsync(client, store.StoreId, store.TerminalId);
        var qr = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { amount = 10, clientRequestId = Guid.NewGuid() });
        var qrId = qr.GetProperty("id").GetInt32();
        await socket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new
        { orderId, qrId, qrDataUrl = "https://attacker.invalid/qr", amount = total, accountNumber = "ATTACKER" } });
        var snapshot = await State(client);
        Assert.Equal("qr", snapshot.GetProperty("mode").GetString());
        Assert.EndsWith("Z", snapshot.GetProperty("validUntilUtc").GetString());
        Assert.True(snapshot.GetProperty("validUntilUtc").GetDateTime() > DateTime.UtcNow);
        Assert.Equal(qr.GetProperty("qrDataUrl").GetString(), snapshot.GetProperty("payload").GetProperty("qrDataUrl").GetString());
        Assert.Equal(10, snapshot.GetProperty("payload").GetProperty("amount").GetDecimal());
        Assert.Equal("123456789", snapshot.GetProperty("payload").GetProperty("accountNumber").GetString());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/payment-qr/{qrId}/manual-confirm", new { });
        var receipt = (await State(client)).GetProperty("receipt").GetProperty("payload");
        Assert.False(receipt.GetProperty("finalized").GetBoolean());
        Assert.Equal(10, receipt.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(total - 10, receipt.GetProperty("remainingAmount").GetDecimal());
        Assert.Contains("thủ công", receipt.GetProperty("confirmationSource").GetString());
        await socket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_hide", payload = new { orderId } });
        Assert.NotEqual(JsonValueKind.Null, (await State(client)).GetProperty("receipt").ValueKind);
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new { orderId, qrId } });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), amount = total - 10 + 5, method = 0 });
        receipt = (await State(client)).GetProperty("receipt").GetProperty("payload");
        Assert.False(receipt.GetProperty("finalized").GetBoolean());
        Assert.Equal(5, receipt.GetProperty("changeAmount").GetDecimal());
        Assert.Contains("tiền mặt", receipt.GetProperty("confirmationSource").GetString());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize");
        receipt = (await State(client)).GetProperty("receipt").GetProperty("payload");
        Assert.True(receipt.GetProperty("finalized").GetBoolean());
        var next = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        Assert.Equal("idle", (await State(client)).GetProperty("mode").GetString());
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_hide", payload = new { orderId } });
    }

    [Fact]
    public async Task Cancelled_expired_changed_recipient_foreign_terminal_and_old_cart_QRs_cannot_be_presented()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, _) = await Start(app, client, store);
        await using var socket = await PosWebSocketClient.ConnectAsync(client, store.StoreId, store.TerminalId);
        var qr = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { amount = 10 });
        var qrId = qr.GetProperty("id").GetInt32();
        Task Show() => socket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new { orderId, qrId } });
        Task Denied() => socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_qr_created", payload = new { orderId, qrId } });
        await Show();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = await db.StoreBankAccounts.SingleAsync(); bank.AccountNumber = "987654321";
            await db.SaveChangesAsync();
        }
        Assert.Equal("idle", (await State(client)).GetProperty("mode").GetString());
        await Denied();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.StoreBankAccounts.SingleAsync()).AccountNumber = "123456789";
            (await db.PosPaymentQrRequests.SingleAsync()).ExpireAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await db.SaveChangesAsync();
        }
        await Denied();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.PosPaymentQrRequests.SingleAsync()).ExpireAtUtc = DateTime.UtcNow.AddMinutes(10);
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "FOREIGN", Name = "Other terminal" };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync();
            var shift = new POSShift { StoreId = store.StoreId, TerminalId = terminal.Id, WarehouseId = store.WarehouseId, OpenedByUserId = (await db.Users.SingleAsync()).Id };
            db.POSShifts.Add(shift); await db.SaveChangesAsync();
            var order = new Order { StoreId = store.StoreId, POSShiftId = shift.Id, GrandTotal = 10 };
            db.Orders.Add(order); await db.SaveChangesAsync();
            (await db.PosPaymentQrRequests.SingleAsync()).OrderId = order.Id;
            await db.SaveChangesAsync();
        }
        await Denied();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.PosPaymentQrRequests.SingleAsync()).OrderId = orderId; await db.SaveChangesAsync();
        }
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/payment-qr/{qrId}/cancel", new { });
        await Denied();
        Assert.Equal("idle", (await State(client)).GetProperty("mode").GetString());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/hold", new { holdNote = "Switch cart test" });
        await Denied();
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Empty(await final.OrderPayments.ToListAsync());
    }

    [Fact]
    public async Task Deposit_display_uses_server_QR_and_committed_receipt_and_does_not_leak_into_next_cart()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, _) = await Start(app, client, store);
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Synthetic deposit customer" };
            db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
        }
        var id = Guid.NewGuid();
        var qr = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive-qr", new { clientRequestId = id, customerId, amount = 100 });
        var state = await State(client);
        Assert.Equal("qr", state.GetProperty("mode").GetString());
        Assert.Equal("deposit", state.GetProperty("payload").GetProperty("kind").GetString());
        Assert.Equal(qr.GetProperty("qrDataUrl").GetString(), state.GetProperty("payload").GetProperty("qrDataUrl").GetString());
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = id, customerId, amount = 100, method = 1, reference = qr.GetProperty("content").GetString(), purpose = "Test deposit" });
        state = await State(client);
        Assert.Equal("recorded", state.GetProperty("mode").GetString());
        Assert.Equal(100, state.GetProperty("payload").GetProperty("paidAmount").GetDecimal());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/hold", new { holdNote = "Next deposit customer test" });
        Assert.Equal("idle", (await State(client)).GetProperty("mode").GetString());
    }

    private static Task<JsonElement> State(FullApplicationFixture.Client client) => client.JsonAsync(HttpMethod.Get, StateUrl);
    private static async Task<(int Id, decimal Total)> Start(FullApplicationFixture app, FullApplicationFixture.Client client, FullApplicationFixture.StoreSeed store)
    {
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.StoreBankAccounts.Add(new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "Synthetic bank",
                AccountNumber = "123456789", AccountName = "Synthetic recipient", VietQrBankBin = "970416",
                IsDefault = true, IsActive = true, QrRenderMode = BankQrRenderMode.LocalEmvQr, ConfirmMode = BankQrConfirmMode.Manual });
            await db.SaveChangesAsync();
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft"); var order = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{order}/items?variantId={store.VariantId}&qty=3");
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        return (order, (await verify.Orders.SingleAsync(o => o.Id == order)).GrandTotal);
    }
}
