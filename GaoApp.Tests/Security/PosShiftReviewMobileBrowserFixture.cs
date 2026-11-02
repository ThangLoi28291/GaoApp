using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only disposable GUID LocalDB data; no menus, permissions or configured store data are changed.
internal static class PosShiftReviewMobileBrowserFixture
{
    internal sealed record Seed(int ShiftId, int SecondShiftId, int ReceivedShiftId, int OpenShiftId,
        FullApplicationFixture.Account Cashier, FullApplicationFixture.Account Denied, FullApplicationFixture.Account Other);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account admin)
    {
        var cashier=await app.AddAccountAsync(store,"*");
        var denied=await app.AddAccountAsync(store,PermissionCodes.Admin.DashboardView);
        var other=await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,app.Stores[1]);
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shifts=new List<POSShift>();
        for(var i=0;i<25;i++)shifts.Add(new POSShift {
            StoreId=store.StoreId,TerminalId=store.TerminalId,WarehouseId=store.WarehouseId,
            OpenedByUserId=i==0?cashier.UserId:admin.UserId,OpenedAtUtc=DateTime.UtcNow.AddHours(-3).AddMinutes(-i),
            ClosedByUserId=i==24?null:i==0?cashier.UserId:admin.UserId,ClosedAtUtc=i==24?null:DateTime.UtcNow.AddHours(-1),
            Status=i==24?POSShiftStatus.Open:POSShiftStatus.Closed,ShiftCode="REVIEW-"+i.ToString("00"),
            OpeningCash=i==0?200000:70000,CashSalesTotal=i==0?26000:0,NonCashSalesTotal=i==0?1000:0,
            CashInTotal=i==0?13000:0,CashOutTotal=i==0?13000:0,ClosingCashExpected=i==0?226000:70000,
            ClosingCashActual=i==24?null:i==0?225000:i%2==0?69000:70000,
            CashReceivedAmount=i>=14&&i<24?70000:null,CashReceivedByUserId=i>=14&&i<24?admin.UserId:null,
            CashReceivedAtUtc=i>=14&&i<24?DateTime.UtcNow:null,CashReceiptNote=i>=14&&i<24?"Đã kiểm trước thử nghiệm":null,
            OpenNote="Mở ca đối soát",CloseNote="Ghi chú <b>chỉ là văn bản</b>" });
        db.AddRange(shifts);await db.SaveChangesAsync();
        for(var i=0;i<26;i++) {
            var order=new Order { StoreId=store.StoreId,POSShiftId=shifts[0].Id,OrderNumber="REVIEW-ORDER-"+i.ToString("00"),
                Status=OrderStatus.Completed,PaymentStatus=PaymentStatus.Paid,Subtotal=i==0?2000:1000,GrandTotal=i==0?2000:1000,
                PaidTotal=i==0?2000:1000,CreatedBy=cashier.UserId,CreatedAtUtc=DateTime.UtcNow.AddHours(-2),CompletedAtUtc=DateTime.UtcNow.AddHours(-1),
                Note="Đối chiếu <script>không thực thi</script>" };
            order.Payments.Add(new OrderPayment {StoreId=store.StoreId,Method=PaymentMethod.Cash,Amount=1000,CreatedBy=cashier.UserId});
            if(i==0)order.Payments.Add(new OrderPayment {StoreId=store.StoreId,Method=PaymentMethod.BankTransfer,Amount=1000,ReferenceCode="MANUAL-REVIEW-1000",CreatedBy=cashier.UserId});
            db.Add(order);
            db.Add(new POSShiftCashTransaction {StoreId=store.StoreId,POSShiftId=shifts[0].Id,CreatedByUserId=cashier.UserId,
                Type=i%2==0?POSShiftCashTransactionType.CashIn:POSShiftCashTransactionType.CashOut,Amount=1000,
                Reason="Phiếu kiểm tra "+i,Note="Nội dung đối soát dài để đọc trên điện thoại; <b>văn bản gốc</b>"});
        }
        await db.SaveChangesAsync();return new(shifts[0].Id,shifts[1].Id,shifts[14].Id,shifts[24].Id,cashier,denied,other);
    }
    internal static async Task<string> FingerprintAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {configuration=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store),
            // Only REVIEW-00 receipt fields/rowversion may change in this browser run.
            shiftCash=await db.POSShifts.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.OpenedByUserId,x.OpeningCash,x.ClosingCashExpected,x.ClosingCashActual,
                x.CashSalesTotal,x.NonCashSalesTotal,x.CashInTotal,x.CashOutTotal,x.CloseNote,x.ClosedByUserId,x.ClosedAtUtc,x.NeedsCashReconciliation}).ToListAsync(),
            otherReceipts=await db.POSShifts.AsNoTracking().Where(x=>x.ShiftCode!="REVIEW-00").OrderBy(x=>x.Id).Select(x=>new{x.Id,x.CashReceivedAmount,x.CashReceivedByUserId,x.CashReceivedAtUtc,x.CashReceiptNote,x.RowVersion}).ToListAsync(),
            orders=await db.Orders.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.GrandTotal,x.PaidTotal,x.RowVersion}).ToListAsync(),
            payments=await db.OrderPayments.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Method,x.Amount,x.ReferenceCode,x.RowVersion}).ToListAsync(),
            cash=await db.POSShiftCashTransactions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Type,x.Amount,x.RowVersion}).ToListAsync(),
            stock=await db.InventoryBalances.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OnHandQty,x.RowVersion}).ToListAsync(),
            slips=await db.POSShiftClosingSlips.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RowVersion}).ToListAsync() });
    }
    internal static async Task<object> VerifyAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store,FullApplicationFixture.Account admin)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shift=await db.POSShifts.SingleAsync(x=>x.ShiftCode=="REVIEW-00");
        if(shift.CashReceivedAmount!=224000||shift.CashReceivedByUserId!=admin.UserId||shift.CashReceivedAtUtc==null
            ||shift.CashReceiptNote!="UI07c kiểm lại thiếu thêm 1.000"||shift.ClosingCashActual!=225000||shift.ClosingCashExpected!=226000)
            throw new InvalidOperationException("Original immutable approval must persist exact actual/expected/received and actor/note.");
        return new {passed=true,shift.Id,shift.ClosingCashExpected,shift.ClosingCashActual,shift.CashReceivedAmount,
            shift.CashReceivedByUserId,shift.CashReceivedAtUtc,shift.CashReceiptNote,expectedDifference=-2000,employeeDifference=-1000,
            originalOrdersPaymentsCashStockSlipsOtherReceiptsAndConfigurationUnchanged=true};
    }
}
