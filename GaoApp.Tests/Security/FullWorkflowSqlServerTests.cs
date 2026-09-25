using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class FullWorkflowSqlServerTests
{
    [Theory]
    [InlineData("user-disabled")]
    [InlineData("password")]
    [InlineData("membership-disabled")]
    [InlineData("role-changed")]
    [InlineData("permission-removed")]
    [InlineData("other-permission-removed")]
    [InlineData("permission-replaced")]
    [InlineData("idle")]
    public async Task Real_cookie_websocket_is_revoked_and_cannot_receive_or_broadcast_after_access_changes(string change)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var target = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Create);
        var sender = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Payment.Create);
        using var targetClient = await app.LoginAsync(target);
        using var senderClient = await app.LoginAsync(sender);
        await using var targetSocket = await PosWebSocketClient.ConnectAsync(targetClient, store.StoreId, store.TerminalId);
        await using var senderSocket = await PosWebSocketClient.ConnectAsync(senderClient, store.StoreId, store.TerminalId);
        await senderSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_preview", payload = new { value = "synthetic" } });
        await targetSocket.WaitForEventAsync("customer_payment_preview");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var membership = await db.UserInStores.Include(x => x.User).Include(x => x.Role).SingleAsync(x => x.UserId == target.UserId);
            switch (change)
            {
                case "user-disabled": case "idle": membership.User.IsActive = false; break;
                case "password": membership.User.PasswordHash = new GaoApp.Infrastructure.Identity.PasswordHasher().Hash("Replacement-synthetic-123!"); break;
                case "membership-disabled": membership.IsActive = false; break;
                case "role-changed": membership.Role.Code += "_CHANGED"; break;
                case "permission-replaced":
                    var grant = await db.RolePermissions.SingleAsync(x => x.RoleId == target.RoleId && x.Permission.Code == PermissionCodes.Pos.Order.View);
                    var permissionId = grant.PermissionId;
                    db.RolePermissions.Remove(grant); await db.SaveChangesAsync();
                    db.RolePermissions.Add(new GaoApp.Domain.Entities.RolePermission { RoleId = target.RoleId, PermissionId = permissionId }); break;
                default:
                    var code = change == "permission-removed" ? PermissionCodes.Pos.Order.View : PermissionCodes.Pos.Order.Create;
                    db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(x => x.RoleId == target.RoleId && x.Permission.Code == code)); break;
            }
            await db.SaveChangesAsync();
        }
        if (change != "idle")
        {
            await senderSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_hide", payload = new { secret = "must-not-reach-target" } });
            await senderSocket.WaitForEventAsync("customer_payment_hide");
        }
        await targetSocket.WaitForCloseAsync();
        Assert.False(targetSocket.HasEvent("customer_payment_hide"));
        await senderSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_display_reset", payload = new { } });
        await senderSocket.WaitForEventAsync("customer_display_reset");
    }

    [Fact]
    public async Task Real_socket_cannot_join_another_store_or_terminal_send_server_events_or_survive_logout()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var other = app.Stores[1];
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var cashier = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Payment.Create);
        using var client = await app.LoginAsync(viewer);
        using var writerClient = await app.LoginAsync(cashier);
        await using var socket = await PosWebSocketClient.ConnectAsync(client, store.StoreId, store.TerminalId);
        await using var writerSocket = await PosWebSocketClient.ConnectAsync(writerClient, store.StoreId, store.TerminalId);
        await socket.InvokeRejectedAsync("JoinStoreGroup", other.StoreId, other.TerminalId.ToString());
        await socket.InvokeRejectedAsync("JoinStoreGroup", store.StoreId, other.TerminalId.ToString());
        await socket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "customer_payment_hide", payload = new { } });
        await writerSocket.InvokeRejectedAsync("BroadcastTerminalEvent", new { eventType = "order_finalized", payload = new { orderId = 1 } });
        using var logout = await client.Http.PostAsync("/admin/account/logout", null);
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        await socket.WaitForCloseAsync();
        await writerSocket.InvokeAsync("BroadcastTerminalEvent", new { eventType = "customer_display_reset", payload = new { } });
        await writerSocket.WaitForEventAsync("customer_display_reset");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Disabling_store_or_terminal_closes_idle_connection(bool disableStore)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        using var client = await app.LoginAsync(viewer);
        await using var socket = await PosWebSocketClient.ConnectAsync(client, store.StoreId, store.TerminalId);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            if (disableStore) (await db.Stores.SingleAsync(x => x.Id == store.StoreId)).IsActive = false;
            else (await db.POSTerminals.SingleAsync(x => x.Id == store.TerminalId)).IsActive = false;
            await db.SaveChangesAsync();
        }
        await socket.WaitForCloseAsync();
    }

    [Fact]
    public async Task Real_login_sale_payment_finalize_return_void_and_two_store_authorization_preserve_money_and_stock()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var other = app.Stores[1];
        var manager = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View);
        var otherManager = await app.AddAccountAsync(other, "*");
        using var client = await app.LoginAsync(manager);
        using var readOnly = await app.LoginAsync(viewer);
        using var foreign = await app.LoginAsync(otherManager);
        await using var storeSocket = await PosWebSocketClient.ConnectAsync(readOnly, store.StoreId, store.TerminalId);
        await using var foreignSocket = await PosWebSocketClient.ConnectAsync(foreign, other.StoreId, other.TerminalId);
        using var anonymous = app.Anonymous(store);
        using (var response = await anonymous.Http.GetAsync("/admin/pos/orders")) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using (var response = await readOnly.Http.PostAsJsonAsync("/admin/pos/draft", new { })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        foreach (var path in new[] { "/admin/pos/1/finalize", "/admin/pos/orders/1/refund", "/admin/pos/returns", "/admin/pos/cart/current/discount", "/admin/pos/cart/current/payments", "/admin/pos/cart/current/payment-and-finalize", "/admin/pos/orders/1/hold", "/admin/pos/shift/close" })
        {
            using var response = await readOnly.Http.PostAsJsonAsync(path, new { });
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"View-only caller was not denied: {path}, {response.StatusCode}");
        }
        using (var replay = app.Anonymous(other))
        {
            replay.Cookies.Add(client.Cookies.GetCookies(app.Address));
            using var response = await replay.Http.GetAsync("/admin/pos/orders");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        using (var request = new HttpRequestMessage(HttpMethod.Post, "/admin/pos/shift/open") { Content = JsonContent.Create(new { openingCash = 0, warehouseId = store.WarehouseId }) })
        {
            var token = client.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
            client.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            try { using var response = await client.Http.SendAsync(request); Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); }
            finally { client.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token); }
        }
        var shift = await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var shiftId = shift.GetProperty("id").GetInt32();
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var orderId = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        using (var response = await foreign.Http.GetAsync($"/admin/pos/{orderId}")) Assert.Contains(response.StatusCode, new[] { HttpStatusCode.NotFound, HttpStatusCode.BadRequest });
        using (var response = await readOnly.Http.PostAsJsonAsync($"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = (int)PaymentMethod.Cash, amount = 60 })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = (int)PaymentMethod.Cash, amount = 60 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize");
        await storeSocket.WaitForEventAsync("order_finalized");
        Assert.False(foreignSocket.HasEvent("order_finalized"));
        // Retry the complete operation. Whether replay succeeds or rejects, it must not post stock again.
        using (var retry = await client.Http.PostAsync($"/admin/pos/{orderId}/finalize", null))
            Assert.Contains(retry.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await db.Orders.Include(x => x.Lines).Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
            Assert.Equal(OrderStatus.Completed, order.Status); Assert.Equal(60, order.GrandTotal); Assert.Equal(60, order.PaidTotal);
            Assert.Single(order.Payments); lineId = Assert.Single(order.Lines).Id;
            Assert.Equal(97, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            Assert.Single(await db.InventoryTransactions.Where(x => x.ReferenceType == InventoryReferenceType.Order && x.ReferenceId == orderId.ToString()).ToListAsync());
        }
        using (var response = await readOnly.Http.PostAsJsonAsync($"/admin/pos/orders/{orderId}/void", new { reason = "forbidden" })) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var returned = await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId, posShiftId = shiftId, type = (int)SalesReturnType.ReturnAndRefund, reason = "E2E return",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 1, returnBaseQuantity = 1, refundUnitAmount = 20, action = (int)SalesReturnLineAction.Restock } },
            payments = new[] { new { method = (int)PaymentMethod.Cash, amount = 20 } } });
        Assert.True(returned.GetProperty("success").GetBoolean());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(98, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            var finalShift = await db.POSShifts.SingleAsync(x => x.Id == shiftId);
            Assert.Equal(40, finalShift.CashSalesTotal - finalShift.CashRefundTotal);
            Assert.Equal(40, finalShift.ClosingCashExpected);
            var refund = Assert.Single(await db.SalesReturns.Include(x => x.Payments).Where(x => x.OrderId == orderId).ToListAsync());
            Assert.Equal(20, refund.RefundTotal); Assert.Equal(20, Assert.Single(refund.Payments).Amount);
        }
        // A separate completed sale can be voided; stock returns to its pre-sale value exactly once.
        var second = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft"); var secondId = second.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{secondId}/items?variantId={store.VariantId}&qty=2");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{secondId}/payments", new { clientRequestId = Guid.NewGuid(), method = (int)PaymentMethod.Cash, amount = 40 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{secondId}/finalize");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{secondId}/void", new { reason = "E2E void" });
        using (var retry = await client.Http.PostAsJsonAsync($"/admin/pos/orders/{secondId}/void", new { reason = "E2E retry void" }))
            Assert.Contains(retry.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.BadRequest, HttpStatusCode.Conflict });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 40, note = "E2E reconciliation" });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(OrderStatus.Voided, (await db.Orders.SingleAsync(x => x.Id == secondId)).Status);
            Assert.Equal(98, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            var closedShift = await db.POSShifts.SingleAsync(x => x.Id == shiftId);
            Assert.Equal(POSShiftStatus.Closed, closedShift.Status);
            Assert.Equal(40, closedShift.ClosingCashExpected);
            Assert.Equal(40, closedShift.ClosingCashActual);
        }
        await using (var db = app.Database.CreateTenantContext(other.StoreId))
        {
            Assert.Empty(await db.Orders.ToListAsync());
            Assert.Equal(100, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == other.VariantId)).OnHandQty);
        }
    }
}
