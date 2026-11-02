using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// All test data, including imported archive examples, stays inside the disposable GUID database.
internal static class PosAfterSalesMobileBrowserFixture
{
    internal sealed record Seed(int ReadyReturnId, int BlockedReturnId, int PhoneOrderId, int StaleOrderId, int ShiftOrderId,
        FullApplicationFixture.Account ReadOnly, FullApplicationFixture.Account Denied, FullApplicationFixture.Account Other);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account account)
    {
        using var client = await app.LoginAsync(account);
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            (await db.Warehouses.SingleAsync(x => x.Id == store.WarehouseId)).AllowNegativeInventory = true;
            (await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId)).ProductVariantName = "Gạo thơm đặc sản · kiểm tra số lượng theo đơn vị gốc";
            await db.SaveChangesAsync();
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 10000, warehouseId = store.WarehouseId });
        async Task<int> Sale(int qty, int method) {
            var id = (await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft")).GetProperty("orderId").GetInt32();
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty={qty}");
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/payments", new {clientRequestId=Guid.NewGuid(),method,amount=qty*20});
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/finalize"); return id;
        }
        var phone = await Sale(1,0); var stale = await Sale(2,0); var shift = await Sale(1,0);
        async Task<int> Pending(int qty) {
            var order = await Sale(qty,1);
            await client.JsonAsync(HttpMethod.Post,$"/admin/pos/orders/{order}/refund",new {reason="UI08 hàng trả chờ xác định giá vốn",refundMethod=0,allowPendingRestock=true});
            return (await client.JsonAsync(HttpMethod.Get,$"/admin/pos/returns/order/{order}/history"))[0].GetProperty("id").GetInt32();
        }
        var ready = await Pending(97);
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            await Inventory.InventoryPosPostingContractTests.CreateRealMovementService(db).CreateAsync(
                new InventoryMovementFactory().CreatePurchaseReceipt(store.WarehouseId,store.VariantId,1,15,"UI08-RESOLVE",1,"UI08 giá vốn đã xác định",1));
        }
        var blocked = await Pending(1);
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) {
            for(var i=1;i<=53;i++) {
                var legacyId = 880000L+i; var name = i==53 ? "Khách %_ nguồn <b>nguyên bản</b>" : "Khách hàng nguồn "+i;
                var header = JsonSerializer.Serialize(new { Note="Ghi chú <script>window.__archiveInjected=true</script> "+new string('x',180) });
                var details = JsonSerializer.Serialize(new[]{new {ID=i,ProductCode="SKU-NGUON-"+new string('A',100),Quantity=2.5m,Price=12345m,Total=30862.5m,Note="Dòng nguồn <b>nguyên bản</b>"}});
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO dbo.LegacyReturnArchives(StoreId,LegacyOrderId,OccurredAtUtc,LegacyCustomerId,CustomerName,LegacyUserId,EmployeeName,SourceTotal,SourcePaymentFlag,HeaderJson,DetailsJson,ImportedAtUtc)
                    VALUES({store.StoreId},{legacyId},{DateTime.UtcNow.AddMinutes(i)},123,{name},456,N'Nhân viên nguồn',{30862.5m},{true},{header},{details},{DateTime.UtcNow})
                    """);
            }
            var foreignId = app.Stores[1].StoreId;
            var emptyHeader = "{}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO dbo.LegacyReturnArchives(StoreId,LegacyOrderId,CustomerName,HeaderJson,DetailsJson,ImportedAtUtc)
                VALUES({foreignId},999001,N'Nguồn cửa hàng khác',{emptyHeader},N'[]',{DateTime.UtcNow})
                """);
        }
        return new(ready,blocked,phone,stale,shift,
            await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View),
            await app.AddAccountAsync(store,PermissionCodes.Admin.DashboardView),await app.AddAccountAsync(app.Stores[1],"*"));
    }
    internal static async Task<string> FingerprintAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {
            config=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store),
            originalOrders=await db.Orders.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.GrandTotal,x.PaidTotal,x.ChangeDue}).ToListAsync(),
            originalPayments=await db.OrderPayments.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OrderId,x.Method,x.Amount,x.PaidAtUtc,x.ClientRequestId,x.RowVersion}).ToListAsync(),
            archive=await db.Database.SqlQueryRaw<string>("SELECT (SELECT * FROM dbo.LegacyReturnArchives ORDER BY Id FOR JSON PATH) AS Value").SingleAsync()
        });
    }
    internal static async Task<object> VerifyAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shifts=await db.POSShifts.OrderBy(x=>x.Id).ToListAsync();
        var stock=(await db.InventoryBalances.SingleAsync(x=>x.ProductVariantId==store.VariantId)).OnHandQty;
        var returns=await db.SalesReturns.CountAsync();var payments=await db.SalesReturnPayments.CountAsync();
        var audit=await db.POSAuditLogs.CountAsync(x=>x.Action=="RETURN_RESTOCK_COMPLETED");
        var pending=await db.SalesReturnRestockFragments.CountAsync(x=>x.CompletedAtUtc==null);
        var restockedLine = await db.SalesReturnLines.SingleAsync(x=>x.Action==SalesReturnLineAction.Restock);
        var completed = await new GaoApp.Infrastructure.Repositories.Orders.SalesReturnRestockRepository(db)
            .GetCompletedQuantitiesAsync(new[]{restockedLine.Id},CancellationToken.None);
        await using var foreignDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        var foreignCompleted = await new GaoApp.Infrastructure.Repositories.Orders.SalesReturnRestockRepository(foreignDb)
            .GetCompletedQuantitiesAsync(new[]{restockedLine.Id},CancellationToken.None);
        if(stock!=96||returns!=5||payments!=5||audit!=1||pending!=1||shifts.Count!=2||shifts[0].CashRefundTotal!=2020||shifts[1].CashRefundTotal!=20
            ||shifts[0].ClosingCashActual!=8060||shifts[1].Status!=POSShiftStatus.Open
            ||completed.GetValueOrDefault((restockedLine.Id,store.WarehouseId))!=97||foreignCompleted.Count!=0)
            throw new InvalidOperationException($"After-sales totals mismatch: stock={stock}, returns={returns}, payments={payments}, audit={audit}, pending={pending}; shifts="+JsonSerializer.Serialize(shifts.Select(x=>new{x.CashRefundTotal,x.ClosingCashActual,x.Status})));
        return new{passed=true,stock,returns,payments,audit,pending,completedBaseQuantity=97,foreignCompletedCount=foreignCompleted.Count,shifts=shifts.Select(x=>new{x.Id,x.Status,x.OpeningCash,x.CashRefundTotal,x.ClosingCashActual}),
            sourcePaymentsArchivesMenusStoreConfigurationAndOtherStoreUnchanged=true,duplicateAndStaleCommandsDoNotPostExtraMoneyOrStock=true};
    }
}
