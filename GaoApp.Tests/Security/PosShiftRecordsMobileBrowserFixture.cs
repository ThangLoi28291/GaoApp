using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// Only the disposable GUID LocalDB fixture. No store/menu seeding is added.
internal static class PosShiftRecordsMobileBrowserFixture
{
    internal sealed record Seed(int FirstShiftId, int SecondShiftId, int OtherCashierShiftId, int SecondTerminalId,
        FullApplicationFixture.Account Cashier, FullApplicationFixture.Account Denied, FullApplicationFixture.Account Other);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store, FullApplicationFixture.Account admin)
    {
        var cashier = await app.AddAccountAsync(store, "*");
        var denied = await app.AddAccountAsync(store, PermissionCodes.Admin.DashboardView);
        var other = await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var secondTerminal = new POSTerminal { StoreId = store.StoreId, Code = "HISTORY-02", Name = "Quầy kiểm tra số 02" };
        db.Add(secondTerminal); await db.SaveChangesAsync();
        var shifts = new List<POSShift>();
        for (var i = 0; i < 25; i++) shifts.Add(new POSShift { StoreId=store.StoreId, TerminalId=i==24?secondTerminal.Id:store.TerminalId,
            WarehouseId=store.WarehouseId, OpenedByUserId=admin.UserId, OpenedAtUtc=DateTime.UtcNow.Date.AddDays(-i).AddHours(1),
            ClosedByUserId=i==24?null:admin.UserId, ClosedAtUtc=i==24?null:DateTime.UtcNow.Date.AddDays(-i).AddHours(2),
            Status=i==24?POSShiftStatus.Open:POSShiftStatus.Closed, ShiftCode="HISTORY-"+i.ToString("00"), OpeningCash=i==0?200000:70000,
            ClosingCashExpected=i==0?200000:70000, ClosingCashActual=i==24?null:i==0?195000:70000,
            CashInTotal=i==0?11000:0, CashOutTotal=i==0?11000:0, NonCashSalesTotal=i==0?22000:0,
            OpenNote="Mở ca kiểm tra "+i, CloseNote=i==24?null:"Ghi chú đóng ca <b>chỉ là văn bản</b>" });
        db.AddRange(shifts);
        var privateShift = new POSShift { StoreId=store.StoreId, TerminalId=store.TerminalId, WarehouseId=store.WarehouseId,
            OpenedByUserId=cashier.UserId, ClosedByUserId=cashier.UserId, Status=POSShiftStatus.Closed, ShiftCode="HISTORY-OTHER-CASHIER",
            OpenedAtUtc=DateTime.UtcNow.AddDays(-1), ClosedAtUtc=DateTime.UtcNow, OpeningCash=50000, ClosingCashExpected=50000, ClosingCashActual=50000 };
        db.Add(privateShift); await db.SaveChangesAsync();
        for(var i=0;i<22;i++) {
            db.Add(new Order { StoreId=store.StoreId, POSShiftId=shifts[0].Id, OrderNumber="HISTORY-ORDER-"+i,
                Status=OrderStatus.Completed, PaymentStatus=PaymentStatus.Paid, Subtotal=1000, GrandTotal=1000, PaidTotal=1000,
                CreatedAtUtc=DateTime.UtcNow.AddHours(-2), CompletedAtUtc=DateTime.UtcNow.AddHours(-1) });
            db.Add(new POSShiftCashTransaction { StoreId=store.StoreId, POSShiftId=shifts[0].Id, CreatedByUserId=admin.UserId,
                Type=i%2==0?POSShiftCashTransactionType.CashIn:POSShiftCashTransactionType.CashOut, Amount=1000,
                Reason="Lý do kiểm tra "+i, Note="Nội dung <script>không thực thi</script>" });
        }
        for(var i=0;i<23;i++) db.Add(new POSShiftHandoverSlip { StoreId=store.StoreId, WarehouseId=store.WarehouseId, TerminalId=secondTerminal.Id,
            CreatedByUserId=admin.UserId, SlipCode="HISTORY-SLIP-"+i.ToString("00"), BarcodeValue="HISTORY-BARCODE-"+i,
            OpeningCashTotal=50000, Status=i==0?POSShiftHandoverSlipStatus.Printed:i==1?POSShiftHandoverSlipStatus.Cancelled:POSShiftHandoverSlipStatus.Draft,
            CreatedAtUtc=DateTime.UtcNow.AddDays(-i-1), Note="Phiếu mẫu kiểm tra "+i });
        await db.SaveChangesAsync();
        return new(shifts[0].Id,shifts[1].Id,privateShift.Id,secondTerminal.Id,cashier,denied,other);
    }
    internal static async Task<string> FingerprintAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new { configuration=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store),
            shifts=await db.POSShifts.AsNoTracking().Where(x=>x.ShiftCode!=null&&x.ShiftCode.StartsWith("HISTORY-")).OrderBy(x=>x.Id)
                .Select(x=>new{x.Id,x.Status,x.OpenedByUserId,x.ClosingCashExpected,x.ClosingCashActual,x.RowVersion}).ToListAsync(),
            orders=await db.Orders.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.POSShiftId,x.Status,x.GrandTotal,x.PaidTotal,x.RowVersion}).ToListAsync(),
            transactions=await db.POSShiftCashTransactions.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Amount,x.Type,x.RowVersion}).ToListAsync(),
            seedSlips=await db.POSShiftHandoverSlips.AsNoTracking().Where(x=>x.SlipCode.StartsWith("HISTORY-")).OrderBy(x=>x.Id)
                .Select(x=>new{x.Id,x.Status,x.OpeningCashTotal,x.BarcodeValue,x.RowVersion}).ToListAsync() });
    }
    internal static async Task<object> VerifyAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var slips=await db.POSShiftHandoverSlips.Where(x=>x.Note!=null&&x.Note.StartsWith("UI07b")).OrderBy(x=>x.Id).ToListAsync();
        if(slips.Count!=2)throw new InvalidOperationException("Expected exactly two intended UI-created slips.");
        var used=slips.Single(x=>x.Note=="UI07b — sửa phiếu 350.000");
        var cancelled=slips.Single(x=>x.Note=="UI07b — hủy phiếu");
        var shift=await db.POSShifts.SingleAsync(x=>x.Id==used.UsedPOSShiftId);
        var denoms=await db.POSShiftHandoverSlipDenominations.Where(x=>x.POSShiftHandoverSlipId==used.Id&&!x.IsDeleted).ToListAsync();
        if(used.Status!=POSShiftHandoverSlipStatus.Used||used.OpeningCashTotal!=350000||shift.OpeningCash!=350000
            ||shift.OpenedByUserId!=used.UsedByUserId||shift.TerminalId!=store.TerminalId||denoms.Sum(x=>x.Amount)!=350000
            ||cancelled.Status!=POSShiftHandoverSlipStatus.Cancelled||cancelled.OpeningCashTotal!=100000||cancelled.CancelReason!="UI07b hủy theo xác nhận"
            ||await db.OrderPayments.AnyAsync()||await db.InventoryBalances.AnyAsync(x=>x.OnHandQty!=100))
            throw new InvalidOperationException("Slip create/edit/receive/cancel must retain the original business and exact cash.");
        return new{passed=true,usedSlipId=used.Id,used.Status,used.OpeningCashTotal,used.UsedByUserId,shiftId=shift.Id,shift.OpeningCash,
            cancelledSlipId=cancelled.Id,cancelled.CancelReason,denominations=denoms.Select(x=>new{x.DenominationValue,x.Quantity,x.Amount}),
            originalHistoryOrdersTransactionsSeedSlipsMenuStoreAndOtherStoreUnchanged=true,noNewPaymentsOrStockChanges=true};
    }
}
