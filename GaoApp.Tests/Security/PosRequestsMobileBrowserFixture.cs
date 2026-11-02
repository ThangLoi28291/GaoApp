using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

// All setup is confined to the existing disposable GUID fixture. No production/menu seed is added.
internal static class PosRequestsMobileBrowserFixture
{
    internal sealed record Seed(int ShiftId,int TransactionId,int OrderId,int CashPaymentId,int BankPaymentId,int DepositEntryId,
        FullApplicationFixture.Account Admin,FullApplicationFixture.Account Peer,FullApplicationFixture.Account Denied,
        FullApplicationFixture.Account ReadOnly,FullApplicationFixture.Account OtherStore);
    internal static async Task<Seed> PrepareAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store,FullApplicationFixture.Account cashier)
    {
        var admin=await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,store);
        var peer=await app.AddAccountAsync(store,"*");
        var denied=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
        var readOnly=await app.AddAccountAsync(store,PermissionCodes.Pos.Shift.View);
        var other=await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,app.Stores[1]);
        int customerId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
            var customer=new Customer {StoreId=store.StoreId,Name="Khách cọc UI08a"};db.Add(customer);
            db.Add(new StoreBankAccount {StoreId=store.StoreId,BankName="Test",BankCode="ACB",AccountNumber="UI08A-TEST",AccountName="Test",
                VietQrBankBin="970416",IsActive=true,IsDefault=true,ConfirmMode=BankQrConfirmMode.Manual});
            await db.SaveChangesAsync();customerId=customer.Id;
        }
        using var employee=await app.LoginAsync(cashier);using var manager=await app.LoginAsync(admin);
        var shift=(await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=200000,warehouseId=store.WarehouseId})).GetProperty("id").GetInt32();
        var transaction=(await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/cash-transaction",new {type=1,amount=10000,reason="Thu bổ sung UI08a",note="Gốc <b>chỉ là văn bản</b>"})).GetProperty("id").GetInt32();
        var order=(await employee.JsonAsync(HttpMethod.Post,"/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{order}/items?variantId={store.VariantId}&qty=3");
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{order}/payments",new {clientRequestId=Guid.NewGuid(),method=0,amount=100});
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{order}/payments",new {clientRequestId=Guid.NewGuid(),method=1,amount=20,referenceCode="UI08A-BANK-ORIGINAL"});
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{order}/finalize");
        var deposit=(await employee.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/receive",new {clientRequestId=Guid.NewGuid(),customerId,amount=50000,method=0,purpose="Cọc UI08a"})).GetProperty("depositId").GetInt32();
        await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/close",new {closingCashActual=259000,note="Kiểm đếm gốc UI08a"});
        await manager.JsonAsync(HttpMethod.Post,$"/admin/pos/shift/{shift}/cash-receipt",new {receivedAmount=258000,note="Bàn giao gốc UI08a"});
        await using var final=app.Database.CreateTenantContext(store.StoreId);
        var cash=await final.OrderPayments.SingleAsync(x=>x.OrderId==order&&x.Method==PaymentMethod.Cash);
        var bank=await final.OrderPayments.SingleAsync(x=>x.OrderId==order&&x.Method==PaymentMethod.BankTransfer);
        var entry=await final.Set<CustomerDepositEntry>().SingleAsync(x=>x.CustomerDepositId==deposit&&x.Kind=="Receive");
        var voucher=await final.POSShiftCashTransactions.SingleAsync(x=>x.Id==transaction);
        var sale=await final.Orders.SingleAsync(x=>x.Id==order);
        // Rejected history only supplies pagination/read evidence; none can be applied by the browser.
        for(var i=0;i<24;i++) {
            final.Add(new POSCashAdjustmentRequest {StoreId=store.StoreId,ClientRequestId=Guid.NewGuid(),TransactionId=transaction,POSShiftId=shift,
                RequestedByUserId=cashier.UserId,RequestedByName=cashier.Name,Status=POSCashAdjustmentStatus.Rejected,RequestReason="UI08a history "+i,
                TransactionVersion=Convert.ToBase64String(voucher.RowVersion),OldType=voucher.Type,OldAmount=voucher.Amount,OldReason=voucher.Reason,
                NewType=voucher.Type,NewAmount=voucher.Amount,NewReason=voucher.Reason,ReviewedByUserId=admin.UserId,ReviewedByName=admin.Name,ReviewedAtUtc=DateTime.UtcNow.AddDays(-1),ReviewNote="Lịch sử từ chối"});
            final.Add(new POSPaymentAdjustmentRequest {StoreId=store.StoreId,ClientRequestId=Guid.NewGuid(),PaymentId=bank.Id,OrderId=order,POSShiftId=shift,
                RequestedByUserId=cashier.UserId,RequestedByName=cashier.Name,Status=POSCashAdjustmentStatus.Rejected,RequestReason="UI08a history "+i,
                PaymentVersion=Convert.ToBase64String(bank.RowVersion),OrderVersion=Convert.ToBase64String(sale.RowVersion),Amount=20,OldMethod=PaymentMethod.BankTransfer,NewMethod=PaymentMethod.Card,
                ReviewedByUserId=admin.UserId,ReviewedByName=admin.Name,ReviewedAtUtc=DateTime.UtcNow.AddDays(-1),ReviewNote="Lịch sử từ chối"});
        }
        await final.SaveChangesAsync();
        return new(shift,transaction,order,cash.Id,bank.Id,entry.Id,admin,peer,denied,readOnly,other);
    }
    internal static async Task<string> FingerprintAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        return JsonSerializer.Serialize(new {configuration=await PosCheckoutMobileBrowserFixture.FingerprintAsync(app,store),
            bankConfiguration=await db.StoreBankAccounts.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.IsActive,x.IsDefault,x.ConfirmMode,x.RowVersion}).ToListAsync(),
            closingEvidence=await db.POSShifts.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OpenedByUserId,x.Status,x.OpeningCash,x.ClosingCashActual,x.CloseNote,x.ClosedAtUtc,x.CashReceivedAmount,x.CashReceivedAtUtc,x.CashReceivedByUserId,x.CashReceiptNote}).ToListAsync(),
            orders=await db.Orders.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.GrandTotal,x.PaidTotal,x.ChangeDue,x.BalanceDue}).ToListAsync(),
            paymentAmounts=await db.OrderPayments.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Amount,x.PaidAtUtc,x.ClientRequestId}).ToListAsync(),
            depositBalances=await db.Set<CustomerDeposit>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Balance,x.RowVersion}).ToListAsync(),
            depositEvidence=await db.Set<CustomerDepositEntry>().AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Kind,x.Amount,x.ClientRequestId,x.RequestJson,x.CreatedAtUtc}).ToListAsync(),
            stock=await db.InventoryBalances.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.OnHandQty,x.RowVersion}).ToListAsync(),
            slips=await db.POSShiftClosingSlips.AsNoTracking().OrderBy(x=>x.Id).Select(x=>new{x.Id,x.ClosingCashExpected,x.ClosingCashActual,x.RowVersion}).ToListAsync(),
            cashHistory=await db.Set<POSCashAdjustmentRequest>().AsNoTracking().Where(x=>x.RequestReason.StartsWith("UI08a history")).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.RowVersion}).ToListAsync(),
            paymentHistory=await db.Set<POSPaymentAdjustmentRequest>().AsNoTracking().Where(x=>x.RequestReason.StartsWith("UI08a history")).OrderBy(x=>x.Id).Select(x=>new{x.Id,x.Status,x.RowVersion}).ToListAsync()});
    }
    internal static async Task<object> VerifyAsync(FullApplicationFixture app,FullApplicationFixture.StoreSeed store)
    {
        await using var db=app.Database.CreateTenantContext(store.StoreId);
        var shift=await db.POSShifts.SingleAsync();
        var cash=await db.Set<POSCashAdjustmentRequest>().Where(x=>!x.RequestReason.StartsWith("UI08a history")).ToListAsync();
        var payments=await db.Set<POSPaymentAdjustmentRequest>().Where(x=>!x.RequestReason.StartsWith("UI08a history")).ToListAsync();
        var entry=await db.Set<CustomerDepositEntry>().SingleAsync(x=>x.Kind=="Receive");
        var depositCash=await db.POSShiftCashTransactions.IgnoreQueryFilters().SingleAsync(x=>x.CustomerDepositEntryId==entry.Id);
        var manual=await db.POSShiftCashTransactions.SingleAsync(x=>x.CustomerDepositEntryId==null);
        var slips=await db.POSShiftClosingSlips.SingleAsync();
        if(cash.Count!=1||cash[0].Status!=POSCashAdjustmentStatus.Approved||payments.Count!=4
            ||payments.Count(x=>x.Status==POSCashAdjustmentStatus.Approved)!=2||payments.Count(x=>x.Status==POSCashAdjustmentStatus.Withdrawn)!=1||payments.Count(x=>x.Status==POSCashAdjustmentStatus.Rejected)!=1
            ||shift.CashInTotal!=0||shift.CashOutTotal!=5000||shift.CashSalesTotal!=0||shift.NonCashSalesTotal!=60||shift.ClosingCashExpected!=195000
            ||shift.ClosingCashActual!=259000||shift.CashReceivedAmount!=258000||shift.NeedsCashReconciliation
            ||manual.Type!=POSShiftCashTransactionType.CashOut||manual.Amount!=5000||entry.Method!=PaymentMethod.BankTransfer||!depositCash.IsDeleted
            ||slips.ClosingCashExpected!=260040||slips.ClosingCashActual!=259000||await db.OrderPayments.AnyAsync(x=>x.Method!=PaymentMethod.BankTransfer)
            ||cash.Any(x=>x.ReconciledAtUtc==null)||payments.Where(x=>x.Status==POSCashAdjustmentStatus.Approved).Any(x=>x.ReconciledAtUtc==null))
            throw new InvalidOperationException("Expected exactly the intended cash/payment/deposit approvals, withdrawal/rejection and reconciliation, preserving original count/receipt/closing slip.");
        return new {passed=true,shift.ClosingCashExpected,shift.ClosingCashActual,shift.CashReceivedAmount,shift.CashSalesTotal,shift.NonCashSalesTotal,shift.CashInTotal,shift.CashOutTotal,
            originalClosingExpected=slips.ClosingCashExpected,originalClosingActual=slips.ClosingCashActual,newCashRequests=cash.Count,newPaymentRequests=payments.Count,approved=3,withdrawn=1,rejected=1,
            depositMethod=entry.Method,depositCashSoftDeleted=depositCash.IsDeleted,originalAmountsPaidDatesDepositBalanceStockClosingEvidenceHistoryMenuStoreAndConfigurationUnchanged=true};
    }
}
