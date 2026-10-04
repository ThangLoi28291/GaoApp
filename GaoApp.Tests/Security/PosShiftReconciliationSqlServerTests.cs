using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftReconciliationSqlServerTests
{
    private const string Url = "/admin/pos-shift/reconciliation/data";

    [Fact]
    public async Task Whole_shift_details_preserve_full_receipts_refunds_deleted_payments_and_pending_adjustments_without_writing_money()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0]; var account = await app.AddAccountAsync(store, "*");
        using var client = await app.LoginAsync(account);
        var shift = await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100, warehouseId = store.WarehouseId });
        var shiftId = shift.GetProperty("id").GetInt32();
        var first = await NewOrder(client, store.VariantId);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{first}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{first}/payments", new { clientRequestId = Guid.NewGuid(), method = 1, amount = 40, referenceCode = "MANUAL-40" });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{first}/finalize");
        var second = await NewOrder(client, store.VariantId);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{second}/payments", new { clientRequestId = Guid.NewGuid(), method = 1, amount = 80, referenceCode = "ACB-80" });
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{second}/finalize");
        var voucher = await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/cash-transaction", new { type = 1, amount = 25, reason = "Bổ sung tiền lẻ" });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/cash-transaction", new { type = 2, amount = 5, reason = "Chi vật tư" });
        var voucherId = voucher.GetProperty("id").GetInt32();
        var original = await client.JsonAsync(HttpMethod.Get, $"/admin/pos-shift/cash-adjustments/transactions?transactionId={voucherId}");
        var version = original.GetProperty("items")[0].GetProperty("rowVersion").GetString();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos-shift/cash-adjustments/transactions/{voucherId}/requests", new {
            clientRequestId = Guid.NewGuid(), rowVersion = version, type = 2, amount = 25, reason = "Kiểm tra lại loại phiếu", requestReason = "Đề nghị đối chiếu phiếu gốc"
        });
        var draftId = await NewOrder(client, store.VariantId);
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{draftId}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 10 });
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var removed = await db.OrderPayments.SingleAsync(x => x.OrderId == draftId);
            removed.IsDeleted = true; removed.DeletedAtUtc = DateTime.UtcNow;
            var previousShift = new POSShift {StoreId=store.StoreId,TerminalId=store.TerminalId,WarehouseId=store.WarehouseId,OpenedByUserId=account.UserId,
                Status=POSShiftStatus.Closed,OpenedAtUtc=DateTime.UtcNow.AddDays(-3),ClosedAtUtc=DateTime.UtcNow.AddDays(-2),ShiftCode="PREVIOUS-RECON"};
            db.POSShifts.Add(previousShift);await db.SaveChangesAsync();
            var previousOrder = new Order {StoreId=store.StoreId,POSShiftId=previousShift.Id,Status=OrderStatus.Completed,GrandTotal=50,OrderNumber="PREVIOUS-SALE"};
            db.Orders.Add(previousOrder);await db.SaveChangesAsync();
            var refund = new SalesReturn {StoreId=store.StoreId,POSShiftId=shiftId,OrderId=previousOrder.Id,Status=SalesReturnStatus.Completed,
                ReturnNumber="RECON-REFUND",Reason="Hoàn tiền đơn ca trước",RefundTotal=15,CreatedByUserId=account.UserId};
            refund.Payments.Add(new SalesReturnPayment {StoreId=store.StoreId,Method=PaymentMethod.Cash,Amount=15});db.SalesReturns.Add(refund);
            var current = await db.POSShifts.SingleAsync(x=>x.Id==shiftId);current.CashRefundTotal=15;current.RecalcExpected();
            var bank = new StoreBankAccount {StoreId=store.StoreId,BankCode="ACB",BankName="Test ACB",AccountNumber="TEST-RECON",AccountName="Test",IsActive=true};
            db.StoreBankAccounts.Add(bank);await db.SaveChangesAsync();
            var customer = new Customer {StoreId=store.StoreId,Name="Khách đối soát cọc và nợ"};
            db.Customers.Add(customer);await db.SaveChangesAsync();
            var deposit = new CustomerDeposit {StoreId=store.StoreId,CustomerId=customer.Id,Purpose="Đặt hàng",Balance=9};
            db.Set<CustomerDeposit>().Add(deposit);await db.SaveChangesAsync();
            db.Set<CustomerDepositEntry>().Add(new() {StoreId=store.StoreId,POSShiftId=shiftId,CustomerDepositId=deposit.Id,
                Kind="Receive",Amount=9,Method=PaymentMethod.BankTransfer,StoreBankAccountId=bank.Id,Reference="DEPOSIT-9",CreatedBy=account.UserId});
            previousOrder.CustomerId=customer.Id;previousOrder.IsCreditSale=true;previousOrder.BalanceDue=43;previousOrder.PaidTotal=7;
            var debt = new CustomerDebtReceipt {StoreId=store.StoreId,POSShiftId=shiftId,CustomerId=customer.Id,Amount=7,
                Method=PaymentMethod.BankTransfer,StoreBankAccountId=bank.Id,Reference="DEBT-7",ClientRequestId=Guid.NewGuid(),RequestJson="{}",CreatedBy=account.UserId};
            debt.Entries.Add(new() {StoreId=store.StoreId,CustomerId=customer.Id,OrderId=previousOrder.Id,Kind="Collection",Amount=-7});
            db.Set<CustomerDebtReceipt>().Add(debt);
            db.OrderPayments.Add(new() {StoreId=store.StoreId,OrderId=previousOrder.Id,Amount=7,Method=PaymentMethod.BankTransfer,
                IsDebtCollection=true,Provider="CUSTOMER_DEBT",ReferenceCode=debt.ClientRequestId.ToString(),PaidAtUtc=DateTime.UtcNow});
            var payment = await db.OrderPayments.SingleAsync(x=>x.OrderId==second);
            var qr = new PosPaymentQrRequest {StoreId=store.StoreId,OrderId=second,BankAccountId=bank.Id,Amount=80,PaymentId=payment.Id,
                RequestCode="ACB-80",Status=PosPaymentQrStatus.Paid,ConfirmMode=BankQrConfirmMode.Callback};
            db.PosPaymentQrRequests.Add(qr);await db.SaveChangesAsync();
            db.Set<AcbQrSession>().Add(new() {StoreId=store.StoreId,OrderId=second,ShiftId=shiftId,TerminalId=store.TerminalId,
                CashierId=account.UserId,QrRequestId=qr.Id,PaymentId=payment.Id,Amount=80,ProviderOrderId="ACB-80",
                Status=AcbSessionStatus.Completed,ConfirmationSource=AcbConfirmationSource.Callback});
            (await db.Orders.SingleAsync(x=>x.Id==first)).CreatedAtUtc=DateTime.UtcNow.AddDays(-2);
            await db.SaveChangesAsync();
        }
        var data = await client.JsonAsync(HttpMethod.Get, Url+$"?shiftId={shiftId}");
        Assert.Equal(125m,data.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(125m,data.GetProperty("detailExpectedCash").GetDecimal());
        Assert.Equal(3,data.GetProperty("orders").GetArrayLength());
        var over = data.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==second);
        Assert.Equal(80m,over.GetProperty("bankReceived").GetDecimal());Assert.Equal(20m,over.GetProperty("surplus").GetDecimal());
        Assert.True(over.GetProperty("payments")[0].GetProperty("bankVerified").GetBoolean());
        var manual = data.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==first);
        Assert.False(manual.GetProperty("payments")[1].GetProperty("bankVerified").GetBoolean());
        Assert.True(data.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==draftId).GetProperty("payments")[0].GetProperty("cancelled").GetBoolean());
        Assert.Equal("RECON-REFUND",data.GetProperty("refunds")[0].GetProperty("number").GetString());
        Assert.Equal(-50m,data.GetProperty("adjustments")[0].GetProperty("expectedDelta").GetDecimal());
        Assert.Equal("Pending",data.GetProperty("adjustments")[0].GetProperty("status").GetString());
        var movements=data.GetProperty("otherMovements").EnumerateArray().ToArray();
        Assert.Equal(2,movements.Length);
        Assert.All(movements,x=>Assert.Equal("Khách đối soát cọc và nợ",x.GetProperty("customer").GetString()));
        var collection=movements.Single(x=>x.GetProperty("kind").GetString()=="DebtCollection");
        Assert.Equal("PREVIOUS-SALE",collection.GetProperty("allocations")[0].GetProperty("number").GetString());
        Assert.Equal(7m,collection.GetProperty("allocations")[0].GetProperty("amount").GetDecimal());
        await client.JsonAsync(HttpMethod.Get, Url+$"?shiftId={shiftId}");
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(125m,(await final.POSShifts.SingleAsync(x=>x.Id==shiftId)).ClosingCashExpected);
        Assert.Equal(80m,(await final.OrderPayments.SingleAsync(x=>x.OrderId==second)).Amount);
        Assert.Equal(POSCashAdjustmentStatus.Pending,(await final.Set<POSCashAdjustmentRequest>().SingleAsync()).Status);
        // A session linked to a different sale must never mark this payment as bank verified.
        (await final.Set<AcbQrSession>().SingleAsync()).OrderId=first;await final.SaveChangesAsync();
        var changed=await client.JsonAsync(HttpMethod.Get,Url+$"?shiftId={shiftId}");
        Assert.False(changed.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==second)
            .GetProperty("payments")[0].GetProperty("bankVerified").GetBoolean());
        Assert.Contains("chưa khớp",changed.GetProperty("orders").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==second)
            .GetProperty("payments")[0].GetProperty("confirmation").GetString());
    }

    [Fact]
    public async Task Employee_cannot_read_another_cashier_or_store_and_shift_view_permission_is_required()
    {
        await using var app = await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        using var owner=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        var shift=await owner.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new{openingCash=100,warehouseId=store.WarehouseId});
        var id=shift.GetProperty("id").GetInt32();
        using var peer=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using(var response=await peer.Http.GetAsync(Url+$"?shiftId={id}")) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        using(var response=await foreign.Http.GetAsync(Url+$"?shiftId={id}")) Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        using var restricted=await app.LoginAsync(await app.AddAccountAsync(store,"pos.order.view"));
        using(var response=await restricted.Http.GetAsync(Url+$"?shiftId={id}")) Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
    }
    private static async Task<int> NewOrder(FullApplicationFixture.Client client,int variant)
    {
        var order=await client.JsonAsync(HttpMethod.Post,"/admin/pos/draft");var id=order.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post,$"/admin/pos/{id}/items?variantId={variant}&qty=3");return id;
    }
}
