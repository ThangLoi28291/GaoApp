using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;
namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerDepositSqlServerTests
{
    [Fact]
    public async Task Receive_replay_use_partial_cash_and_void_restore_without_double_cash()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store);
        var receive = new { clientRequestId = Guid.NewGuid(), customerId, amount = 100, method = 0, purpose = "Đặt ba sản phẩm" };
        var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", receive)));
        var depositId = results[0].GetProperty("depositId").GetInt32();
        Assert.Single(results.Select(x => x.GetProperty("depositId").GetInt32()).Distinct());
        using (var mismatch = await client.Http.PostAsJsonAsync("/admin/customer-deposit/receive", new { receive.clientRequestId, customerId, amount = 101, method = 0, purpose = receive.purpose }))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await client.JsonAsync(HttpMethod.Post, $"/admin/customer-deposit/orders/{orderId}/select", new { expectedCustomerId = customerId, depositId, amount = 40 });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            Assert.Equal(100m, (await db.Set<CustomerDeposit>().SingleAsync()).Balance);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), amount = 20, method = 0 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(60m, (await db.Set<CustomerDeposit>().SingleAsync()).Balance);
            var shift = await db.POSShifts.SingleAsync();
            Assert.Equal(100m, shift.CashInTotal); Assert.Equal(20m, shift.CashSalesTotal); Assert.Equal(120m, shift.ClosingCashExpected);
            Assert.Equal(60m, (await db.Orders.SingleAsync(x => x.Id == orderId)).PaidTotal);
            Assert.Single(await db.OrderPayments.ToListAsync());
        }
        using (var repeated = await client.Http.PostAsync($"/admin/pos/{orderId}/finalize", null)) Assert.False(repeated.IsSuccessStatusCode);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/void", new { reason = "Hủy đơn đã dùng cọc" });
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100m, (await verify.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.Equal(100m, await verify.Set<CustomerDepositEntry>().SumAsync(x => x.Amount));
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).CashSalesTotal);
        using var page = await client.Http.GetAsync($"/admin/customer-deposit?customerId={customerId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("PCOC-", await page.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task Bank_deposit_uses_manual_default_and_refund_is_idempotent()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store);
        int bankId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "VCB", BankName = "Ngân hàng mặc định", AccountNumber = "DEPOSIT-TEST", AccountName = "Test", VietQrBankBin = "970436", IsDefault = true, IsActive = true, ConfirmMode = BankQrConfirmMode.Manual };
            db.StoreBankAccounts.Add(bank);
            db.StoreBankAccounts.Add(new() { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB tự động", AccountNumber = "AUTO", AccountName = "Test", IsActive = true, ConfirmMode = BankQrConfirmMode.Callback });
            await db.SaveChangesAsync(); bankId = bank.Id;
        }
        var qrRequestId = Guid.NewGuid();
        var qr = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive-qr", new { clientRequestId = qrRequestId, customerId, amount = 100 });
        Assert.StartsWith("data:image/png;base64,", qr.GetProperty("qrDataUrl").GetString());
        Assert.StartsWith("COC", qr.GetProperty("content").GetString());
        using (var activeQr = await client.Http.GetAsync("/admin/customer-deposit/active-qr"))
        {
            Assert.Equal(HttpStatusCode.OK, activeQr.StatusCode);
            Assert.Equal(qr.GetProperty("content").GetString(), (await activeQr.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("content").GetString());
        }
        var result = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = qrRequestId, customerId, amount = 100, method = 1, purpose = "Đặt hàng", reference = qr.GetProperty("content").GetString() });
        using (var clearedQr = await client.Http.GetAsync("/admin/customer-deposit/active-qr")) Assert.Equal(HttpStatusCode.NoContent, clearedQr.StatusCode);
        var depositId = result.GetProperty("depositId").GetInt32();
        var refund = new { clientRequestId = Guid.NewGuid(), depositId, amount = 30, method = 1, reference = "BANK-OUT", note = "Hoàn một phần" };
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/refund", refund);
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/refund", refund);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(70m, (await verify.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.All(await verify.Set<CustomerDepositEntry>().ToListAsync(), e => Assert.Equal(bankId, e.StoreBankAccountId));
        Assert.Equal(2, await verify.Set<CustomerDepositEntry>().CountAsync());
        Assert.Empty(await verify.PosPaymentQrRequests.ToListAsync());
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).NonCashSalesTotal);
        (await verify.StoreBankAccounts.SingleAsync(x => x.Id == bankId)).ConfirmMode = BankQrConfirmMode.Callback;
        await verify.SaveChangesAsync();
        using var blocked = await client.Http.PostAsJsonAsync("/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 10, method = 1, purpose = "Đặt hàng", reference = "BLOCK" });
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }
    [Fact]
    public async Task Stale_deposit_cannot_finalize_and_unused_deposit_can_be_refunded()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store);
        var received = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 60, method = 0, purpose = "Đặt hàng" });
        var depositId = received.GetProperty("depositId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/customer-deposit/orders/{orderId}/select", new { expectedCustomerId = customerId, depositId, amount = 60 });
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/refund", new { clientRequestId = Guid.NewGuid(), depositId, amount = 60, method = 0, note = "Khách hủy đặt hàng" });
        using (var stale = await client.Http.PostAsync($"/admin/pos/{orderId}/finalize", null)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var excess = await client.Http.PostAsJsonAsync("/admin/customer-deposit/refund", new { clientRequestId = Guid.NewGuid(), depositId, amount = 1, method = 0, note = "Vượt" })) Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.CustomerDeposit.View));
        using (var denied = await viewer.Http.PostAsJsonAsync("/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 1, method = 0, purpose = "Không quyền" })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var crossStore = await foreign.Http.PostAsJsonAsync("/admin/customer-deposit/refund", new { clientRequestId = Guid.NewGuid(), depositId, amount = 1, method = 0, note = "Khác cửa hàng" })) Assert.Equal(HttpStatusCode.BadRequest, crossStore.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(OrderStatus.Draft, (await db.Orders.SingleAsync(x => x.Id == orderId)).Status);
        Assert.Equal(100m, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        Assert.Equal(0m, (await db.POSShifts.SingleAsync()).ClosingCashExpected);
    }
    [Fact]
    public async Task Deposit_with_credit_and_return_refunds_only_settled_amount()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store);
        var result = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 20, method = 0, purpose = "Đặt hàng" });
        await client.JsonAsync(HttpMethod.Post, $"/admin/customer-deposit/orders/{orderId}/select", new { expectedCustomerId = customerId, depositId = result.GetProperty("depositId").GetInt32(), amount = 20 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 40, dueDate = DateTime.UtcNow.AddDays(7).Date });
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) { lineId = await db.OrderLines.Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync(); }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new { orderId, type = 3, reason = "Trả toàn bộ", lines = new[] { new { orderLineId = lineId, returnQuantity = 3, refundUnitAmount = 20, action = 0 } }, payments = new[] { new { method = 0, amount = 20 } } });
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(0m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Equal(0m, (await verify.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.Equal(20m, (await verify.POSShifts.SingleAsync()).CashRefundTotal);
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).ClosingCashExpected);
    }
    [Fact]
    public async Task Returned_goods_can_restore_deposit_without_cash_out_or_double_refund()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store);
        var result = await client.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId, amount = 60, method = 0, purpose = "Đặt hàng" });
        await client.JsonAsync(HttpMethod.Post, $"/admin/customer-deposit/orders/{orderId}/select", new { expectedCustomerId = customerId, depositId = result.GetProperty("depositId").GetInt32(), amount = 60 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize");
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) { lineId = await db.OrderLines.Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync(); }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new { orderId, type = 3, reason = "Hoàn vào cọc", depositRefundAmount = 40, lines = new[] { new { orderLineId = lineId, returnQuantity = 2, refundUnitAmount = 20, action = 0 } }, payments = Array.Empty<object>() });
        using (var excess = await client.Http.PostAsJsonAsync("/admin/pos/returns", new { orderId, type = 3, reason = "Hoàn vượt", depositRefundAmount = 30, lines = new[] { new { orderLineId = lineId, returnQuantity = 1, refundUnitAmount = 20, action = 0 } }, payments = Array.Empty<object>() })) Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new { orderId, type = 3, reason = "Hoàn nốt vào cọc", depositRefundAmount = 20, lines = new[] { new { orderLineId = lineId, returnQuantity = 1, refundUnitAmount = 20, action = 0 } }, payments = Array.Empty<object>() });
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(60m, (await verify.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.Equal(60m, await verify.SalesReturns.SumAsync(x => x.DepositRestoredTotal));
        Assert.Empty(await verify.SalesReturnPayments.ToListAsync());
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).CashRefundTotal);
        Assert.Equal(60m, (await verify.POSShifts.SingleAsync()).ClosingCashExpected);
    }
    private static async Task<(int OrderId, int CustomerId)> Start(FullApplicationFixture app, FullApplicationFixture.Client client, FullApplicationFixture.StoreSeed store)
    {
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách đặt cọc", HaveDebt = true };
            db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft"); var id = draft.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=3");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
        return (id, customerId);
    }
}
