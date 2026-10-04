using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSPaymentAdjustmentSqlServerTests
{
    private const string Base="/admin/pos-shift/payment-adjustments";
    private static Task<JsonElement> Detail(FullApplicationFixture.Client c,int id)=>c.JsonAsync(HttpMethod.Get,Base+$"/{id}");
    private static async Task<JsonElement> Candidate(FullApplicationFixture.Client c,int id)=>(await c.JsonAsync(HttpMethod.Get,Base+$"/payments?paymentId={id}")).GetProperty("items")[0];
    private static object Input(JsonElement p,int method=1,string? reference="BANK-CORRECTED",Guid? key=null)=>new {
        clientRequestId=key??Guid.NewGuid(),rowVersion=p.GetProperty("rowVersion").GetString(),method,reference,requestReason="Nhập nhầm phương thức, đã kiểm tra chứng từ thực nhận"
    };
    private static object Decision(JsonElement d,string? note=null)=>new {rowVersion=d.GetProperty("request").GetProperty("rowVersion").GetString(),shiftRowVersion=d.GetProperty("shiftRowVersion").GetString(),note};
    private static async Task<int> Send(FullApplicationFixture.Client c,int p,object body)=>(await c.JsonAsync(HttpMethod.Post,Base+$"/payments/{p}/requests",body)).GetProperty("id").GetInt32();
    private static async Task Expect(FullApplicationFixture.Client c,string path,object body,HttpStatusCode status)
    {using var response=await c.Http.PostAsJsonAsync(path,body);Assert.True(response.StatusCode==status,$"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");}
    private static async Task<int> Open(FullApplicationFixture.Client c,FullApplicationFixture.StoreSeed s)=>(await c.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=100,warehouseId=s.WarehouseId})).GetProperty("id").GetInt32();
    private static async Task<(int Order,int Cash,int Bank)> Sale(FullApplicationFixture app,FullApplicationFixture.Client c,decimal cash,decimal bank)
    {
        var s=app.Stores[0];var id=(await c.JsonAsync(HttpMethod.Post,"/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await c.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/items?variantId={s.VariantId}&qty=3");
        if(cash>0)await c.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/payments",new {clientRequestId=Guid.NewGuid(),method=0,amount=cash});
        if(bank>0)await c.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/payments",new {clientRequestId=Guid.NewGuid(),method=1,amount=bank,referenceCode="BANK-ORIGINAL"});
        await c.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/finalize");
        await using var db=app.Database.CreateTenantContext(s.StoreId);var rows=await db.OrderPayments.Where(x=>x.OrderId==id).ToListAsync();
        return (id,rows.SingleOrDefault(x=>x.Method==PaymentMethod.Cash)?.Id??0,rows.SingleOrDefault(x=>x.Method==PaymentMethod.BankTransfer)?.Id??0);
    }
    [Fact]
    public async Task Owner_request_and_concurrent_admin_approval_preserve_full_surplus_and_move_only_applied_sales_once()
    {
        await using var app=await FullApplicationFixture.StartAsync();var s=app.Stores[0];
        using var employee=await app.LoginAsync(await app.AddAccountAsync(s,"*"));
        using var peer=await app.LoginAsync(await app.AddAccountAsync(s,"*"));
        using var admin=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,s));
        using var foreign=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,app.Stores[1]));
        await Open(employee,s);var sale=await Sale(app,employee,100,20);
        var p=await Candidate(employee,sale.Cash);var body=Input(p,1,null);var id=await Send(employee,sale.Cash,body);
        Assert.Equal(id,await Send(employee,sale.Cash,body));
        await Expect(employee,Base+$"/payments/{sale.Bank}/requests",Input(await Candidate(employee,sale.Bank),0,null),HttpStatusCode.Conflict);
        await Expect(peer,Base+$"/payments/{sale.Cash}/requests",body,HttpStatusCode.Forbidden);
        Assert.Empty((await peer.JsonAsync(HttpMethod.Get,Base+"/data")).GetProperty("items").EnumerateArray());
        using(var response=await peer.Http.GetAsync(Base+$"/{id}"))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using(var response=await foreign.Http.GetAsync(Base+$"/{id}"))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        var d=await Detail(admin,id);Assert.Equal(-40m,d.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        Assert.Equal(140m,d.GetProperty("currentShift").GetProperty("expected").GetDecimal());Assert.Equal(100m,d.GetProperty("proposedShift").GetProperty("expected").GetDecimal());
        await Expect(employee,Base+$"/{id}/approve",Decision(d),HttpStatusCode.Forbidden);
        DateTime originalPaid;await using(var db=app.Database.CreateTenantContext(s.StoreId))
        {Assert.Equal(PaymentMethod.Cash,(await db.OrderPayments.SingleAsync(x=>x.Id==sale.Cash)).Method);originalPaid=(await db.OrderPayments.SingleAsync(x=>x.Id==sale.Cash)).PaidAtUtc;}
        var responses=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>admin.Http.PostAsJsonAsync(Base+$"/{id}/approve",Decision(d))));
        foreach(var response in responses){using(response)Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());}
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{id}/approve",Decision(d));Assert.Equal(id,await Send(employee,sale.Cash,body));
        await using(var db=app.Database.CreateTenantContext(s.StoreId))
        {
            var shift=await db.POSShifts.SingleAsync();Assert.Equal(0m,shift.CashSalesTotal);Assert.Equal(60m,shift.NonCashSalesTotal);Assert.Equal(100m,shift.ClosingCashExpected);
            var paid=await db.OrderPayments.SingleAsync(x=>x.Id==sale.Cash);Assert.Equal(100m,paid.Amount);Assert.Equal(PaymentMethod.BankTransfer,paid.Method);Assert.Equal(originalPaid,paid.PaidAtUtc);
            Assert.Equal(120m,(await db.Orders.SingleAsync()).PaidTotal);Assert.Single(await db.Set<POSPaymentAdjustmentRequest>().ToListAsync());
        }
        // The other receipt can move to cash without affecting cash sales: the 100 bank receipt already covers the 60 sale.
        var second=await Send(employee,sale.Bank,Input(await Candidate(employee,sale.Bank),0,null));
        var secondDetail=await Detail(admin,second);Assert.Equal(0m,secondDetail.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{second}/approve",Decision(secondDetail));
        var recon=await employee.JsonAsync(HttpMethod.Get,"/admin/pos-shift/reconciliation/data");
        Assert.Equal(100m,recon.GetProperty("detailExpectedCash").GetDecimal());Assert.Equal(60m,recon.GetProperty("orders")[0].GetProperty("surplus").GetDecimal());
        Assert.Equal(2,recon.GetProperty("paymentAdjustments").GetArrayLength());
    }
    [Fact]
    public async Task Closed_shift_preserves_count_receipt_and_slip_and_reconciles_cash_and_payment_adjustments_together()
    {
        await using var app=await FullApplicationFixture.StartAsync();var s=app.Stores[0];
        using var employee=await app.LoginAsync(await app.AddAccountAsync(s,"*"));using var admin=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,s));
        var shiftId=await Open(employee,s);var sale=await Sale(app,employee,60,0);
        var voucher=(await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/cash-transaction",new {type=1,amount=10,reason="Gốc"})).GetProperty("id").GetInt32();
        await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/close",new {closingCashActual=170,note="Bảng đếm gốc"});
        await admin.JsonAsync(HttpMethod.Post,$"/admin/pos/shift/{shiftId}/cash-receipt",new {receivedAmount=169,note="Nhận tiền thực tế"});
        var cashBase="/admin/pos-shift/cash-adjustments";
        var cashRow=(await employee.JsonAsync(HttpMethod.Get,cashBase+$"/transactions?transactionId={voucher}")).GetProperty("items")[0];
        var cashId=(await employee.JsonAsync(HttpMethod.Post,cashBase+$"/transactions/{voucher}/requests",new {
            clientRequestId=Guid.NewGuid(),rowVersion=cashRow.GetProperty("rowVersion").GetString(),type=2,amount=10,reason="Chi thực tế",requestReason="Nhập nhầm thu"
        })).GetProperty("id").GetInt32();
        var cashD=await admin.JsonAsync(HttpMethod.Get,cashBase+$"/{cashId}");
        await admin.JsonAsync(HttpMethod.Post,cashBase+$"/{cashId}/approve",new {rowVersion=cashD.GetProperty("request").GetProperty("rowVersion").GetString()});
        var id=await Send(employee,sale.Cash,Input(await Candidate(employee,sale.Cash)));var d=await Detail(admin,id);
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{id}/approve",Decision(d));d=await Detail(admin,id);
        Assert.Equal(90m,d.GetProperty("currentShift").GetProperty("expected").GetDecimal());Assert.True(d.GetProperty("currentShift").GetProperty("needsReconciliation").GetBoolean());
        var staleShiftVersion=d.GetProperty("shiftRowVersion").GetString();
        var referenceId=await Send(employee,sale.Cash,Input(await Candidate(employee,sale.Cash),1,"BANK-REFERENCE-RECHECKED"));
        var referenceDetail=await Detail(admin,referenceId);Assert.Equal(0m,referenceDetail.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{referenceId}/approve",Decision(referenceDetail));
        await Expect(admin,Base+$"/shifts/{shiftId}/reconcile",new {rowVersion=staleShiftVersion,note="Màn hình trước khi sửa mã giao dịch"},HttpStatusCode.Conflict);
        d=await Detail(admin,id);
        await Expect(employee,Base+$"/shifts/{shiftId}/reconcile",new {rowVersion=d.GetProperty("shiftRowVersion").GetString(),note="Kiểm tra"},HttpStatusCode.Forbidden);
        await admin.JsonAsync(HttpMethod.Post,Base+$"/shifts/{shiftId}/reconcile",new {rowVersion=d.GetProperty("shiftRowVersion").GetString(),note="Đã đối chiếu tiền và chứng từ cả hai điều chỉnh"});
        await using var db=app.Database.CreateTenantContext(s.StoreId);var current=await db.POSShifts.SingleAsync();
        Assert.Equal(170m,current.ClosingCashActual);Assert.Equal(169m,current.CashReceivedAmount);Assert.False(current.NeedsCashReconciliation);
        var slip=await db.POSShiftClosingSlips.SingleAsync();Assert.Equal(170m,slip.ClosingCashExpected);Assert.Equal(170m,slip.ClosingCashActual);Assert.Equal(60m,slip.CashSalesTotal);
        Assert.All(await db.Set<POSPaymentAdjustmentRequest>().ToListAsync(),r=>Assert.NotNull(r.ReconciledAtUtc));Assert.NotNull((await db.Set<POSCashAdjustmentRequest>().SingleAsync()).ReconciledAtUtc);
    }
    [Fact]
    public async Task Validation_stale_order_and_shift_rejection_withdrawal_and_reference_only_changes_cannot_move_money_early()
    {
        await using var app=await FullApplicationFixture.StartAsync();var s=app.Stores[0];
        using var employee=await app.LoginAsync(await app.AddAccountAsync(s,"*"));using var admin=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,s));
        await Open(employee,s);var sale=await Sale(app,employee,0,60);var p=await Candidate(employee,sale.Bank);
        var withoutReference=await Send(employee,sale.Bank,Input(p,1,null));
        await employee.JsonAsync(HttpMethod.Post,Base+$"/{withoutReference}/withdraw",Decision(await Detail(employee,withoutReference)));
        await Expect(employee,Base+$"/payments/{sale.Bank}/requests",new {clientRequestId=Guid.NewGuid(),rowVersion=p.GetProperty("rowVersion").GetString(),method=0,requestReason=" "},HttpStatusCode.BadRequest);
        var body=Input(p,0,null);var id=await Send(employee,sale.Bank,body);var d=await Detail(admin,id);
        await using(var db=app.Database.CreateTenantContext(s.StoreId)){(await db.Orders.SingleAsync()).Note="Chứng từ được cập nhật sau yêu cầu";await db.SaveChangesAsync();}
        await Expect(admin,Base+$"/{id}/approve",Decision(d),HttpStatusCode.Conflict);Assert.False((await Detail(admin,id)).GetProperty("canApprove").GetBoolean());
        await Expect(admin,Base+$"/{id}/reject",Decision(await Detail(admin,id)),HttpStatusCode.BadRequest);
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{id}/reject",Decision(await Detail(admin,id),"Kiểm tra lại chứng từ"));
        var withdrawal=await Send(employee,sale.Bank,Input(await Candidate(employee,sale.Bank),0,null));
        await employee.JsonAsync(HttpMethod.Post,Base+$"/{withdrawal}/withdraw",Decision(await Detail(employee,withdrawal)));
        var reference=await Send(employee,sale.Bank,Input(await Candidate(employee,sale.Bank),1,"BANK-REFERENCE-NEW"));d=await Detail(admin,reference);
        await employee.JsonAsync(HttpMethod.Post,"/admin/pos/shift/cash-transaction",new {type=1,amount=1,reason="Phát sinh sau khi quản lý mở chi tiết"});
        await Expect(admin,Base+$"/{reference}/approve",Decision(d),HttpStatusCode.Conflict);
        d=await Detail(admin,reference);Assert.Equal(0m,d.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{reference}/approve",Decision(d));
        await using var final=app.Database.CreateTenantContext(s.StoreId);var paid=await final.OrderPayments.SingleAsync();
        Assert.Equal(60m,paid.Amount);Assert.Equal("BANK-REFERENCE-NEW",paid.ReferenceCode);Assert.Equal(PaymentMethod.BankTransfer,paid.Method);
        Assert.Equal(101m,(await final.POSShifts.SingleAsync()).ClosingCashExpected);
        Assert.Equal(new[]{POSCashAdjustmentStatus.Withdrawn,POSCashAdjustmentStatus.Rejected,POSCashAdjustmentStatus.Withdrawn,POSCashAdjustmentStatus.Approved},(await final.Set<POSPaymentAdjustmentRequest>().OrderBy(x=>x.Id).ToListAsync()).Select(x=>x.Status));
    }
    [Fact]
    public async Task Deposit_budget_credit_balance_and_paid_refunds_remain_unchanged_when_original_tender_is_corrected()
    {
        await using var app=await FullApplicationFixture.StartAsync();var s=app.Stores[0];
        using var employee=await app.LoginAsync(await app.AddAccountAsync(s,"*"));using var admin=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,s));
        await Open(employee,s);int customer;
        await using(var db=app.Database.CreateTenantContext(s.StoreId)){var c=new Customer {StoreId=s.StoreId,Name="Khách cọc và nợ",HaveDebt=true};db.Customers.Add(c);await db.SaveChangesAsync();customer=c.Id;}
        async Task<int> Draft()
        {
            var order=(await employee.JsonAsync(HttpMethod.Post,"/admin/pos/draft")).GetProperty("orderId").GetInt32();
            await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{order}/items?variantId={s.VariantId}&qty=3");
            await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/cart/current/customer/{customer}",new {repriceExistingLines=false});return order;
        }
        async Task<int> Correct(int order,decimal expectedDelta)
        {
            int payment;await using(var db=app.Database.CreateTenantContext(s.StoreId))payment=await db.OrderPayments.Where(x=>x.OrderId==order).Select(x=>x.Id).SingleAsync();
            var id=await Send(employee,payment,Input(await Candidate(employee,payment)));var d=await Detail(admin,id);
            Assert.Equal(expectedDelta,d.GetProperty("request").GetProperty("expectedDelta").GetDecimal());await admin.JsonAsync(HttpMethod.Post,Base+$"/{id}/approve",Decision(d));return payment;
        }
        var depositOrder=await Draft();
        var deposit=(await employee.JsonAsync(HttpMethod.Post,"/admin/customer-deposit/receive",new {clientRequestId=Guid.NewGuid(),customerId=customer,amount=30,method=0,purpose="Cọc"})).GetProperty("depositId").GetInt32();
        await employee.JsonAsync(HttpMethod.Post,$"/admin/customer-deposit/orders/{depositOrder}/select",new {expectedCustomerId=customer,depositId=deposit,amount=30});
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{depositOrder}/payments",new {clientRequestId=Guid.NewGuid(),method=0,amount=50});
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{depositOrder}/finalize");var depositPayment=await Correct(depositOrder,-30);
        var refundSale=await Sale(app,employee,60,0);
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/orders/{refundSale.Order}/refund",new {reason="Hoàn tiền khách đã nhận",refundMethod=0});
        await Correct(refundSale.Order,-60);
        var credit=await Draft();await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{credit}/payments",new {clientRequestId=Guid.NewGuid(),method=0,amount=20});
        await employee.JsonAsync(HttpMethod.Post,$"/admin/pos/{credit}/finalize-credit",new {clientRequestId=Guid.NewGuid(),expectedCustomerId=customer,expectedBalance=40});
        await Correct(credit,-20);
        await using var final=app.Database.CreateTenantContext(s.StoreId);
        Assert.Equal(50m,(await final.OrderPayments.SingleAsync(x=>x.Id==depositPayment)).Amount);
        Assert.Equal(30m,(await final.Orders.SingleAsync(x=>x.Id==depositOrder)).DepositAmount);Assert.Equal(0m,(await final.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.Equal(40m,(await final.Orders.SingleAsync(x=>x.Id==credit)).BalanceDue);Assert.Equal(40m,await final.Set<CustomerReceivableEntry>().SumAsync(x=>x.Amount));
        Assert.Equal(60m,await final.SalesReturnPayments.SumAsync(x=>x.Amount));Assert.Equal(PaymentMethod.Cash,(await final.SalesReturnPayments.SingleAsync()).Method);
        var shift=await final.POSShifts.SingleAsync();Assert.Equal(0m,shift.CashSalesTotal);Assert.Equal(110m,shift.NonCashSalesTotal);
        Assert.Equal(30m,shift.CashInTotal);Assert.Equal(60m,shift.CashRefundTotal);Assert.Equal(70m,shift.ClosingCashExpected);
        var recon=await employee.JsonAsync(HttpMethod.Get,"/admin/pos-shift/reconciliation/data");Assert.Equal(70m,recon.GetProperty("detailExpectedCash").GetDecimal());
    }
    [Fact]
    public async Task Linked_bank_confirmation_and_debt_receipts_cannot_be_relabelled_even_after_a_request_was_created()
    {
        await using var app=await FullApplicationFixture.StartAsync();var s=app.Stores[0];var account=await app.AddAccountAsync(s,"*");
        using var employee=await app.LoginAsync(account);using var admin=await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app,s));
        var shiftId=await Open(employee,s);var sale=await Sale(app,employee,0,80);
        var id=await Send(employee,sale.Bank,Input(await Candidate(employee,sale.Bank),0,null));
        await using(var db=app.Database.CreateTenantContext(s.StoreId))
        {
            var bank=new StoreBankAccount {StoreId=s.StoreId,BankCode="ACB",BankName="Test",AccountNumber="TEST-PAYMENT-ADJUST",AccountName="Test",IsActive=true};db.StoreBankAccounts.Add(bank);await db.SaveChangesAsync();
            var qr=new PosPaymentQrRequest {StoreId=s.StoreId,OrderId=sale.Order,BankAccountId=bank.Id,Amount=80,PaymentId=sale.Bank,RequestCode="BANK-ORIGINAL",ConfirmMode=BankQrConfirmMode.Callback,Status=PosPaymentQrStatus.Paid};
            db.PosPaymentQrRequests.Add(qr);await db.SaveChangesAsync();
            db.Set<AcbQrSession>().Add(new(){StoreId=s.StoreId,OrderId=sale.Order,ShiftId=shiftId,TerminalId=s.TerminalId,CashierId=account.UserId,
                QrRequestId=qr.Id,PaymentId=sale.Bank,Amount=80,ProviderOrderId="BANK-ORIGINAL",Status=AcbSessionStatus.Completed,ConfirmationSource=AcbConfirmationSource.Callback});await db.SaveChangesAsync();
        }
        Assert.False((await Candidate(employee,sale.Bank)).GetProperty("canRequest").GetBoolean());
        await using(var db=app.Database.CreateTenantContext(s.StoreId))
        {
            (await db.PosPaymentQrRequests.SingleAsync()).IsDeleted=true;
            (await db.Set<AcbQrSession>().SingleAsync()).IsDeleted=true;await db.SaveChangesAsync();
        }
        Assert.False((await Candidate(employee,sale.Bank)).GetProperty("canRequest").GetBoolean());
        await Expect(admin,Base+$"/{id}/approve",Decision(await Detail(admin,id)),HttpStatusCode.Conflict);
        await admin.JsonAsync(HttpMethod.Post,Base+$"/{id}/reject",Decision(await Detail(admin,id),"Đã có chứng từ ngân hàng tự động"));
        await Expect(employee,Base+$"/payments/{sale.Bank}/requests",Input(await Candidate(employee,sale.Bank),0,null),HttpStatusCode.Conflict);
        var cash=await Sale(app,employee,60,0);
        await using(var db=app.Database.CreateTenantContext(s.StoreId)){(await db.OrderPayments.SingleAsync(x=>x.Id==cash.Cash)).IsDebtCollection=true;await db.SaveChangesAsync();}
        // Direct API calls are also checked, even though the UI does not offer debt collection changes.
        await Expect(employee,Base+$"/payments/{cash.Cash}/requests",new {clientRequestId=Guid.NewGuid(),rowVersion="stale",method=1,reference="X",requestReason="Nhầm"},HttpStatusCode.Conflict);
        await using var final=app.Database.CreateTenantContext(s.StoreId);Assert.Equal(PaymentMethod.BankTransfer,(await final.OrderPayments.SingleAsync(x=>x.Id==sale.Bank)).Method);
    }
}
