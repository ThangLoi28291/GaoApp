using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSReturnsSqlServerTests
{
    [Fact]
    public async Task Full_pending_refund_pays_once_and_manager_completion_is_authorized_scoped_and_idempotent()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        await using (var setup = app.Database.CreateTenantContext(store.StoreId))
        { (await setup.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).AllowNegativeInventory = true; await setup.SaveChangesAsync(); }
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new {openingCash = 10000, warehouseId = store.WarehouseId});
        var orderId = await Sale(cashier, store.VariantId, 101, 1, 2020);
        await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/orders/{orderId}/refund", new {reason = "Đã nhận hàng trả, chờ nhập kho", refundMethod = 0, allowPendingRestock = true});
        var history = await cashier.JsonAsync(HttpMethod.Get, $"/admin/pos/returns/order/{orderId}/history");
        var returnId = history[0].GetProperty("id").GetInt32(); Assert.True(history[0].GetProperty("hasPendingRestock").GetBoolean());
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store,
            GaoApp.Application.Common.Security.PermissionCodes.Pos.Order.View,
            GaoApp.Application.Common.Security.PermissionCodes.Inventory.StockDocument.Approve));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, GaoApp.Application.Common.Security.PermissionCodes.Pos.Order.View));
        using (var denied = await employee.Http.PostAsync($"/admin/pos/returns/{returnId}/complete-restock", null)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var denied = await foreign.Http.PostAsync($"/admin/pos/returns/{returnId}/complete-restock", null)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var denied = await manager.Http.PostAsync($"/admin/pos/returns/{returnId}/complete-restock", null)) Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        using (var blocked = await manager.Http.PostAsync($"/admin/pos/returns/{returnId}/complete-restock", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
            var error = await blocked.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal("POS_RETURN_COST_PENDING", error.GetProperty("errorCode").GetString());
        }
        using (var page = await manager.Http.GetAsync("/admin/pos/returns/pending-restock"))
        {
            Assert.True(page.IsSuccessStatusCode); var html = await page.Content.ReadAsStringAsync(); Assert.Contains("js-complete-restock", html);
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults/pos-returns"));
            Directory.CreateDirectory(output); await File.WriteAllTextAsync(Path.Combine(output, "pending-restock-blocked.html"), html);
        }
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(OrderStatus.Refunded, (await db.Orders.SingleAsync(x => x.Id == orderId)).Status);
            Assert.Equal(-1, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            Assert.Equal(2020, (await db.POSShifts.SingleAsync()).CashRefundTotal);
            Assert.Equal(0, await db.InventoryTransactions.CountAsync(x => x.TransactionType == InventoryTransactionType.CustomerReturnIn));
            await GaoApp.Tests.Inventory.InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                new GaoApp.Application.Services.Inventory.InventoryMovementFactory().CreatePurchaseReceipt(store.WarehouseId, store.VariantId, 1, 15, "RESOLVE-PENDING", 1, "RESOLVE-PENDING", 1));
        }
        using (var page = await manager.Http.GetAsync("/admin/pos/returns/pending-restock"))
        {
            Assert.True(page.IsSuccessStatusCode);
            var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../TestResults/pos-returns"));
            await File.WriteAllTextAsync(Path.Combine(output, "pending-restock-ready.html"), await page.Content.ReadAsStringAsync());
        }
        await Task.WhenAll(manager.JsonAsync(HttpMethod.Post, $"/admin/pos/returns/{returnId}/complete-restock"),
            manager.JsonAsync(HttpMethod.Post, $"/admin/pos/returns/{returnId}/complete-restock"));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(101, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            var shift = await db.POSShifts.SingleAsync(); Assert.Equal(2020, shift.CashRefundTotal); Assert.Equal(1, shift.RefundCount);
            Assert.Equal(1, await db.SalesReturns.CountAsync()); Assert.Equal(1, await db.SalesReturnPayments.CountAsync());
            Assert.Equal(0, await db.SalesReturnRestockFragments.CountAsync(x => x.CompletedAtUtc == null));
            Assert.Equal(1, await db.POSAuditLogs.CountAsync(x => x.Action == "RETURN_RESTOCK_COMPLETED"));
        }
        history = await cashier.JsonAsync(HttpMethod.Get, $"/admin/pos/returns/order/{orderId}/history");
        Assert.False(history[0].GetProperty("hasPendingRestock").GetBoolean());
    }

    private static async Task<int> Sale(FullApplicationFixture.Client client, int variantId, int qty, int method, decimal amount)
    {
        var id = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={variantId}&qty={qty}");
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/payments", new {clientRequestId=Guid.NewGuid(), method, amount});
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/finalize");
        return id;
    }

    [Fact]
    public async Task Full_refund_uses_actual_tender_preserves_transfer_surplus_and_does_not_post_twice()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store=app.Stores[0];
        using var client=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        await client.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=1000,warehouseId=store.WarehouseId});
        foreach(var (method,received,refundMethod) in new[]{(0,20m,1),(1,25m,0)})
        {
            var id=await Sale(client,store.VariantId,1,method,received);
            var eligibility=await client.JsonAsync(HttpMethod.Get,$"/admin/pos/returns/order/{id}/eligibility");
            Assert.True(eligibility.GetProperty("lines")[0].GetProperty("canRestock").GetBoolean());
            await client.JsonAsync(HttpMethod.Post,$"/admin/pos/orders/{id}/refund",new {reason="Trả toàn bộ, hoàn theo thực tế",refundMethod});
            using var retry=await client.Http.PostAsJsonAsync($"/admin/pos/orders/{id}/refund",new {reason="Lặp hoàn tiền",refundMethod});
            Assert.Equal(HttpStatusCode.BadRequest,retry.StatusCode);
            await using var db=app.Database.CreateTenantContext(store.StoreId);
            var order=await db.Orders.Include(x=>x.Payments).SingleAsync(x=>x.Id==id);
            Assert.Equal(OrderStatus.Refunded,order.Status);Assert.Equal(received,order.PaidTotal);
            Assert.Equal((PaymentMethod)method,Assert.Single(order.Payments).Method);
            var r=await db.SalesReturns.Include(x=>x.Payments).SingleAsync(x=>x.OrderId==id);
            Assert.Equal(received,r.RefundTotal);Assert.Equal((PaymentMethod)refundMethod,Assert.Single(r.Payments).Method);
            Assert.Equal(100,(await db.InventoryBalances.SingleAsync(x=>x.ProductVariantId==store.VariantId)).OnHandQty);
        }
        await using var verify=app.Database.CreateTenantContext(store.StoreId);var shift=await verify.POSShifts.SingleAsync();
        Assert.Equal(25,shift.CashRefundTotal);Assert.Equal(20,shift.NonCashRefundTotal);Assert.Equal(2,shift.RefundCount);
    }

    [Fact]
    public async Task Refund_only_needs_no_lines_posts_no_stock_and_rejects_over_refund_invalid_tender_and_mixed_return_types()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        using var client=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        await client.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=1000,warehouseId=store.WarehouseId});
        var orderId=await Sale(client,store.VariantId,2,0,40);
        await client.JsonAsync(HttpMethod.Post,"/admin/pos/returns",new {orderId,type=1,reason="Hoàn tiền, không nhận lại hàng",payments=new[]{new {method=1,amount=10m}}});
        var eligibility=await client.JsonAsync(HttpMethod.Get,$"/admin/pos/returns/order/{orderId}/eligibility");
        Assert.Equal(30,eligibility.GetProperty("refundableRemaining").GetDecimal());
        Assert.Equal(2,eligibility.GetProperty("lines")[0].GetProperty("returnableQuantity").GetDecimal());
        var lineId=eligibility.GetProperty("lines")[0].GetProperty("orderLineId").GetInt32();
        var line=new {orderLineId=lineId,returnQuantity=1,refundUnitAmount=20,action=1};
        foreach(var invalid in new object[]{
            new {orderId,type=1,reason="Hoàn quá số đã thu",payments=new[]{new {method=1,amount=31}}},
            new {orderId,type=1,reason="Phương thức sai",payments=new[]{new {method=999,amount=1}}},
            new {orderId,type=2,reason="Trả hàng kèm tiền sai loại",lines=new[]{line},payments=new[]{new {method=0,amount=1}}},
            new {orderId,type=1,reason="Hoàn tiền kèm hàng sai loại",lines=new[]{line},payments=new[]{new {method=0,amount=1}}}})
        {using var rejected=await client.Http.PostAsJsonAsync("/admin/pos/returns",invalid);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);}
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(98,(await db.InventoryBalances.SingleAsync(x=>x.ProductVariantId==store.VariantId)).OnHandQty);
        Assert.Equal(1,await db.SalesReturns.CountAsync());Assert.Equal(0,await db.SalesReturnLines.CountAsync());
        Assert.Equal(0,await db.InventoryTransactions.CountAsync(x=>x.TransactionType==InventoryTransactionType.CustomerReturnIn));
        Assert.Equal(10,(await db.POSShifts.SingleAsync()).NonCashRefundTotal);
    }

    [Fact]
    public async Task Pending_cost_is_clear_in_full_and_partial_returns_and_rolls_back_all_money_and_stock()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        await using(var setup=app.Database.CreateTenantContext(store.StoreId))
        {(await setup.Warehouses.SingleAsync(x=>x.Id==store.WarehouseId)).AllowNegativeInventory=true;await setup.SaveChangesAsync();}
        using var client=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        await client.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=10000,warehouseId=store.WarehouseId});
        var orderId=await Sale(client,store.VariantId,101,1,2020);
        var eligibility=await client.JsonAsync(HttpMethod.Get,$"/admin/pos/returns/order/{orderId}/eligibility");var line=eligibility.GetProperty("lines")[0];
        Assert.False(line.GetProperty("canRestock").GetBoolean());Assert.Equal("POS_RETURN_COST_PENDING",line.GetProperty("restockBlockCode").GetString());
        foreach(var (url,input) in new (string,object)[]{
            ($"/admin/pos/orders/{orderId}/refund",new {reason="Đơn âm kho chưa chốt giá vốn",refundMethod=0}),
            ("/admin/pos/returns",new {orderId,type=3,reason="Trả một món âm kho",lines=new[]{new {orderLineId=line.GetProperty("orderLineId").GetInt32(),returnQuantity=1,returnBaseQuantity=1,refundUnitAmount=20,action=1}},payments=new[]{new {method=0,amount=20}}})})
        {
            using var rejected=await client.Http.PostAsJsonAsync(url,input);Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            var error=await rejected.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("POS_RETURN_COST_PENDING",error.GetProperty("errorCode").GetString());
            Assert.Contains("tạm tính",error.GetProperty("message").GetString());Assert.Contains("Quản lý",error.GetProperty("actionHint").GetString());
        }
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(OrderStatus.Completed,(await db.Orders.SingleAsync(x=>x.Id==orderId)).Status);
        Assert.Equal(0,await db.SalesReturns.CountAsync());Assert.Equal(0,await db.SalesReturnLines.CountAsync());
        Assert.Equal(0,(await db.POSShifts.SingleAsync()).RefundCount);
        Assert.Equal(-1,(await db.InventoryBalances.SingleAsync(x=>x.ProductVariantId==store.VariantId)).OnHandQty);
    }
}
