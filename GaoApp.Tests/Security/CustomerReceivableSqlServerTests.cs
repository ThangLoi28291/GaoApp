using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerReceivableSqlServerTests
{
    [Fact]
    public async Task Credit_sale_collection_in_later_shift_is_idempotent_and_does_not_restate_sales()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, true);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        var credit = new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 40, note = "Ghi nợ không hẹn ngày tại POS" };
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        int firstShift;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await db.Orders.SingleAsync(x => x.Id == orderId);
            firstShift = order.POSShiftId;
            Assert.Equal(OrderStatus.Completed, order.Status);
            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
            Assert.Equal(40m, order.BalanceDue);
            Assert.Null(order.CreditDueDate);
            Assert.Single(await db.Set<CustomerReceivableEntry>().ToListAsync());
            Assert.Equal(97m, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 20 });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var collect = new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 10, method = 0 };
        var responses = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => client.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", collect)));
        Assert.Single(responses.Select(x => x.GetProperty("receiptId").GetInt32()).Distinct());
        using (var excess = await client.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 31, method = 0 }))
            Assert.Equal(HttpStatusCode.BadRequest, excess.StatusCode);
        using (var mismatch = await client.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { collect.clientRequestId, customerId, orderId, amount = 11, method = 0 }))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using (var page = await client.Http.GetAsync($"/admin/customer-debt?customerId={customerId}"))
        {
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("PTCN-", await page.Content.ReadAsStringAsync());
        }
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var crossStore = await foreign.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 1, method = 0 }))
            Assert.Equal(HttpStatusCode.BadRequest, crossStore.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(30m, (await verify.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
        Assert.Equal(30m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Single(await verify.Set<CustomerDebtReceipt>().ToListAsync());
        Assert.Equal(20m, (await verify.POSShifts.SingleAsync(x => x.Id == firstShift)).CashSalesTotal);
        var current = await verify.POSShifts.SingleAsync(x => x.Status == POSShiftStatus.Open);
        Assert.Equal(0m, current.CashSalesTotal);
        Assert.Equal(10m, current.CashInTotal);
        Assert.Equal(10m, current.ClosingCashExpected);
        Assert.Single(await verify.OrderPayments.Where(x => x.IsDebtCollection).ToListAsync());
    }

    [Fact]
    public async Task Credit_requires_existing_customer_permission_and_matching_customer_and_balance()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, false);
        var credit = new { clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60, dueDate = DateTime.UtcNow.AddDays(7).Date };
        using (var blocked = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", credit)) Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { (await db.Customers.SingleAsync(x => x.Id == customerId)).HaveDebt = true; await db.SaveChangesAsync(); }
        using (var stale = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", new { credit.clientRequestId, expectedCustomerId = customerId + 1, expectedBalance = 60, credit.dueDate })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var viewer = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View, PermissionCodes.Pos.Order.Finalize, PermissionCodes.CustomerDebt.View));
        using (var denied = await viewer.Http.PostAsJsonAsync($"/admin/pos/{orderId}/finalize-credit", credit)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await viewer.Http.PostAsJsonAsync("/admin/customer-debt/collect", new { clientRequestId = Guid.NewGuid(), customerId, amount = 1, method = 0 })) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", credit);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(60m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Empty(await verify.OrderPayments.ToListAsync());
        Assert.Equal(0m, (await verify.POSShifts.SingleAsync()).CashSalesTotal);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/void", new { reason = "Hủy đơn nợ chưa thu tiền" });
        verify.ChangeTracker.Clear();
        Assert.Equal(0m, (await verify.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
        Assert.Equal(0m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
        Assert.Equal(100m, (await verify.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
    }

    [Fact]
    public async Task Bank_collection_and_returns_separate_debt_reduction_from_actual_refund()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var (orderId, customerId) = await Start(app, client, store, true);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/finalize-credit", new {
            clientRequestId = Guid.NewGuid(), expectedCustomerId = customerId, expectedBalance = 60,
            dueDate = DateTime.UtcNow.AddDays(7).Date });
        int bankId, lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB", AccountNumber = "TEST-123", AccountName = "Test", IsActive = true };
            db.StoreBankAccounts.Add(bank); await db.SaveChangesAsync(); bankId = bank.Id;
            lineId = await db.OrderLines.Where(x => x.OrderId == orderId).Select(x => x.Id).SingleAsync();
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/customer-debt/collect", new {
            clientRequestId = Guid.NewGuid(), customerId, orderId, amount = 20, method = 1, storeBankAccountId = bankId, reference = "BANK-001" });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId, type = 3, reason = "Trả hàng trừ công nợ",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 2, refundUnitAmount = 20, action = 0 } }, payments = Array.Empty<object>() });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(0m, (await db.Orders.SingleAsync(x => x.Id == orderId)).BalanceDue);
            Assert.Equal(-40m, (await db.Set<CustomerReceivableEntry>().SingleAsync(x => x.Kind == "Return")).Amount);
            Assert.Empty(await db.SalesReturnPayments.ToListAsync());
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId, type = 3, reason = "Trả phần còn lại, hoàn tiền đã thu",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 1, refundUnitAmount = 20, action = 0 } },
            payments = new[] { new { method = 1, amount = 20, referenceCode = "REFUND-001" } } });
        using (var page = await client.Http.GetAsync($"/admin/customer-debt?customerId={customerId}"))
        {
            var html = await page.Content.ReadAsStringAsync();
            Assert.True(page.IsSuccessStatusCode, html);
            Assert.Contains("REFUND-001", html);
        }
        using (var export = await client.Http.GetAsync($"/admin/customer-debt/export?customerId={customerId}"))
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        var shift = await verify.POSShifts.SingleAsync();
        Assert.Equal(0m, shift.CashInTotal);
        Assert.Equal(0m, shift.NonCashSalesTotal);
        Assert.Equal(20m, shift.NonCashRefundTotal);
        Assert.Equal(0m, await verify.Set<CustomerReceivableEntry>().SumAsync(x => x.Amount));
    }

    private static async Task<(int OrderId, int CustomerId)> Start(FullApplicationFixture app, FullApplicationFixture.Client client, FullApplicationFixture.StoreSeed store, bool eligible)
    {
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách công nợ", HaveDebt = eligible };
            db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var order = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var id = order.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=3");
        var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
        Assert.Equal(eligible, draft.GetProperty("customerCanBuyOnCredit").GetBoolean());
        return (id, customerId);
    }
}
