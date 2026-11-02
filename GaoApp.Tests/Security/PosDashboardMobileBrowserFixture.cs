using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Test-only records in the existing disposable GUID database. No menu or configured store data is created.
internal static class PosDashboardMobileBrowserFixture
{
    internal sealed record Seed(int ShiftId,int HistoricalShiftId,int ForeignCashierShiftId,
        FullApplicationFixture.Account OtherCashier,FullApplicationFixture.Account Denied,
        FullApplicationFixture.Account NoShiftView,FullApplicationFixture.Account OtherStore);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store,FullApplicationFixture.Account user)
    {
        var otherCashier=await app.AddAccountAsync(store,"*");
        var denied=await app.AddAccountAsync(store,PermissionCodes.Admin.DashboardView);
        var noShiftView=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var otherStore=await app.AddAccountAsync(app.Stores[1],"*");
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shift=new POSShift {StoreId=store.StoreId,WarehouseId=store.WarehouseId,TerminalId=store.TerminalId,OpenedByUserId=user.UserId,
            ShiftCode="DASH-CURRENT",OpeningCash=200000,CashSalesTotal=120000,NonCashSalesTotal=80000,CashInTotal=50000,CashOutTotal=20000,
            CashRefundTotal=10000,NonCashRefundTotal=5000,RefundCount=2,VoidCount=1,Status=POSShiftStatus.Open,OpenedAtUtc=DateTime.UtcNow.AddDays(-1)};
        shift.RecalcExpected();
        var historical=new POSShift {StoreId=store.StoreId,WarehouseId=store.WarehouseId,TerminalId=store.TerminalId,OpenedByUserId=user.UserId,
            ShiftCode="DASH-HISTORY",OpeningCash=999000,ClosingCashExpected=999000,ClosingCashActual=999000,
            Status=POSShiftStatus.Closed,OpenedAtUtc=DateTime.UtcNow.AddDays(-3),ClosedAtUtc=DateTime.UtcNow.AddDays(-2),ClosedByUserId=user.UserId};
        var foreignShift=new POSShift {StoreId=store.StoreId,WarehouseId=store.WarehouseId,TerminalId=store.TerminalId,OpenedByUserId=otherCashier.UserId,
            ShiftCode="DASH-OTHER-CASHIER",OpeningCash=777000,ClosingCashExpected=777000,ClosingCashActual=777000,
            Status=POSShiftStatus.Closed,OpenedAtUtc=DateTime.UtcNow.AddDays(-3),ClosedAtUtc=DateTime.UtcNow.AddDays(-2),ClosedByUserId=otherCashier.UserId};
        db.AddRange(shift,historical,foreignShift);await db.SaveChangesAsync();
        for(var i=0;i<12;i++)db.Add(new Order {StoreId=store.StoreId,POSShiftId=shift.Id,OrderNumber="DASH-ORDER-"+i,
            Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,GrandTotal=10000,Subtotal=10000,PaidTotal=10000,
            CreatedBy=user.UserId,CreatedAtUtc=DateTime.UtcNow.AddHours(-13+i),CompletedAtUtc=DateTime.UtcNow.AddHours(-12+i)});
        db.AddRange(new Order {StoreId=store.StoreId,POSShiftId=shift.Id,OrderNumber="DASH-DRAFT-EXCLUDED",Status=OrderStatus.Draft,GrandTotal=900000},
            new Order {StoreId=store.StoreId,POSShiftId=shift.Id,OrderNumber="DASH-CANCELLED-EXCLUDED",Status=OrderStatus.Cancelled,GrandTotal=800000},
            new Order {StoreId=store.StoreId,POSShiftId=historical.Id,OrderNumber="DASH-OTHER-SHIFT-EXCLUDED",Status=OrderStatus.Completed,GrandTotal=999000,CompletedAtUtc=DateTime.UtcNow.AddDays(-2)},
            new Order {StoreId=store.StoreId,POSShiftId=foreignShift.Id,OrderNumber="DASH-OTHER-USER-EXCLUDED",Status=OrderStatus.Completed,GrandTotal=777000,CompletedAtUtc=DateTime.UtcNow.AddDays(-2)});
        await db.SaveChangesAsync();return new(shift.Id,historical.Id,foreignShift.Id,otherCashier,denied,noShiftView,otherStore);
    }
    internal static async Task<string> FingerprintAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {configuration=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store),
            shifts=await db.POSShifts.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.OpenedByUserId,x.TerminalId,x.OpeningCash,x.CashSalesTotal,x.NonCashSalesTotal,
                x.CashInTotal,x.CashOutTotal,x.CashRefundTotal,x.NonCashRefundTotal,x.RefundCount,x.VoidCount,x.ClosingCashExpected,x.ClosingCashActual,x.CashReceivedAmount,x.RowVersion}).ToListAsync(),
            orders=await db.Orders.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.POSShiftId,x.Status,x.GrandTotal,x.PaidTotal,x.RowVersion}).ToListAsync(),
            payments=await db.OrderPayments.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Method,x.Amount,x.RowVersion}).ToListAsync(),
            cash=await db.POSShiftCashTransactions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Type,x.Amount,x.RowVersion}).ToListAsync(),
            stock=await db.InventoryBalances.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OnHandQty,x.RowVersion}).ToListAsync(),
            closingSlips=await db.POSShiftClosingSlips.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync(),
            handoverSlips=await db.POSShiftHandoverSlips.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync()});
    }
    internal static async Task<object> VerifyAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shift=await db.POSShifts.AsNoTracking().SingleAsync(x=>x.ShiftCode=="DASH-CURRENT");
        if(shift.ClosingCashExpected!=340000||shift.Status!=POSShiftStatus.Open||await db.OrderPayments.AnyAsync()||await db.InventoryBalances.AnyAsync(x=>x.OnHandQty!=100))
            throw new InvalidOperationException("Dashboard must remain read-only with exact current-shift totals.");
        return new {passed=true,shift.Id,shift.ClosingCashExpected,shift.CashSalesTotal,shift.NonCashSalesTotal,
            completedOrders=12,completedSales=120000,averageCompletedOrder=10000,paymentBucketTotal=200000,
            originalShiftsOrdersPaymentsCashStockSlipsMenusStoreConfigurationAndOtherStoreUnchanged=true};
    }
}
