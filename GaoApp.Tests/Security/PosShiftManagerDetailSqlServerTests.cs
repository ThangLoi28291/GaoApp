using System.Net;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftManagerDetailSqlServerTests
{
    [Fact]
    public async Task Manager_sees_verified_payment_channels_capped_revenue_cash_evidence_and_explicit_unmatched_totals()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var employee = await app.AddAccountAsync(store, "*");
        using var cashier = await app.LoginAsync(employee);
        var opened = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100, warehouseId = store.WarehouseId });
        var shiftId = opened.GetProperty("id").GetInt32();
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Order Sale(string number, decimal grand, OrderStatus status = OrderStatus.Completed) => new()
            { StoreId = store.StoreId, POSShiftId = shiftId, OrderNumber = number, GrandTotal = grand, Status = status, CreatedBy = employee.UserId };
            void Pay(Order order, PaymentMethod method, decimal amount, string? reference = null, bool deleted = false, bool debt = false)
                => order.Payments.Add(new OrderPayment { StoreId = store.StoreId, Method = method, Amount = amount, ReferenceCode = reference,
                    IsDeleted = deleted, IsDebtCollection = debt, CreatedBy = employee.UserId });
            var mixed = Sale("MIXED-100", 100); mixed.DepositAmount = 10;
            Pay(mixed, PaymentMethod.BankTransfer, 25, "MANUAL-25"); Pay(mixed, PaymentMethod.BankTransfer, 40, "ACB-40");
            Pay(mixed, PaymentMethod.Card, 30); Pay(mixed, PaymentMethod.Cash, 50);
            Pay(mixed, PaymentMethod.BankTransfer, 500, deleted: true); Pay(mixed, PaymentMethod.BankTransfer, 777, debt: true);
            var cash = Sale("CASH-50", 50); Pay(cash, PaymentMethod.Cash, 60);
            var offline = Sale("OFFLINE-20", 20); Pay(offline, PaymentMethod.BankTransfer, 20, "ACB-20");
            var unknown = Sale("CHECK-30", 30); Pay(unknown, PaymentMethod.BankTransfer, 30, "ACB-30");
            var draft = Sale("DRAFT-999", 999, OrderStatus.Draft); Pay(draft, PaymentMethod.BankTransfer, 999);
            db.AddRange(mixed, cash, offline, unknown, draft); await db.SaveChangesAsync();
            mixed.Payments.Single(p => p.Amount == 500).IsDeleted = true;
            await db.SaveChangesAsync();
            var bank = new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "ACB test", AccountNumber = "TEST-MANAGER",
                AccountName = "Test", IsActive = true, ConfirmMode = BankQrConfirmMode.Callback };
            db.Add(bank); await db.SaveChangesAsync();
            async Task Session(Order order, AcbConfirmationSource? source)
            {
                var p = order.Payments.Single(x => x.Method == PaymentMethod.BankTransfer && x.ReferenceCode?.StartsWith("ACB-") == true);
                var qr = new PosPaymentQrRequest { StoreId = store.StoreId, OrderId = order.Id, BankAccountId = bank.Id, PaymentId = p.Id,
                    Amount = p.Amount, RequestCode = p.ReferenceCode!, Status = PosPaymentQrStatus.Paid, ConfirmMode = BankQrConfirmMode.Callback };
                db.Add(qr); await db.SaveChangesAsync();
                var session = new AcbQrSession { StoreId = store.StoreId, OrderId = order.Id, ShiftId = shiftId, TerminalId = store.TerminalId,
                    CashierId = employee.UserId, QrRequestId = qr.Id, PaymentId = p.Id, Amount = p.Amount, ProviderOrderId = p.ReferenceCode!,
                    Status = AcbSessionStatus.Completed, ConfirmationSource = source };
                db.Add(session);
                await db.SaveChangesAsync();
                if (source == AcbConfirmationSource.Callback)
                {
                    // Archived bank evidence still explains an active historic payment.
                    session.IsDeleted = true; qr.IsDeleted = true; await db.SaveChangesAsync();
                }
            }
            await Session(mixed, AcbConfirmationSource.Callback); await Session(offline, AcbConfirmationSource.OfflineManual); await Session(unknown, null);
            db.POSShiftCashTransactions.AddRange(new POSShiftCashTransaction { StoreId = store.StoreId, POSShiftId = shiftId,
                Type = POSShiftCashTransactionType.CashIn, Amount = 20, Reason = "Bổ sung", CreatedByUserId = employee.UserId },
                new POSShiftCashTransaction { StoreId = store.StoreId, POSShiftId = shiftId,
                Type = POSShiftCashTransactionType.CashOut, Amount = 10, Reason = "Chi vật tư", CreatedByUserId = employee.UserId });
            var refund = new SalesReturn { StoreId = store.StoreId, POSShiftId = shiftId, OrderId = cash.Id, Status = SalesReturnStatus.Completed,
                ReturnNumber = "MANAGER-REFUND", Reason = "Trả hàng", RefundTotal = 5, CreatedByUserId = employee.UserId };
            refund.Payments.Add(new SalesReturnPayment { StoreId = store.StoreId, Method = PaymentMethod.Cash, Amount = 5 }); db.Add(refund);
            var shift = await db.POSShifts.SingleAsync(x => x.Id == shiftId);
            shift.CashSalesTotal = 50; shift.NonCashSalesTotal = 145; shift.CashInTotal = 20; shift.CashOutTotal = 10; shift.CashRefundTotal = 5;
            shift.RefundCount = 1; shift.RecalcExpected(); shift.Status = POSShiftStatus.Closed; shift.ClosingCashActual = 170; shift.ClosedAtUtc = DateTime.UtcNow;
            shift.CloseNote = "Cần kiểm tra khoản dư"; await db.SaveChangesAsync();
        }
        var dashboard = await admin.JsonAsync(HttpMethod.Get, "/admin/pos/shift/manager-dashboard");
        var overview = dashboard.GetProperty("overview"); var detail = overview.GetProperty("paymentBreakdown");
        Assert.Equal(140m, detail.GetProperty("detailNonCashSalesTotal").GetDecimal());
        Assert.Equal(5m, detail.GetProperty("difference").GetDecimal());
        var channels = detail.GetProperty("channels").EnumerateArray().ToDictionary(x => x.GetProperty("channel").GetString()!);
        Assert.Equal(45m, channels["BankManual"].GetProperty("salesAmount").GetDecimal());
        Assert.Equal(40m, channels["BankAutomatic"].GetProperty("salesAmount").GetDecimal());
        Assert.Equal(25m, channels["Card"].GetProperty("salesAmount").GetDecimal());
        Assert.Equal(30m, channels["Card"].GetProperty("recordedAmount").GetDecimal());
        Assert.Equal(30m, channels["BankUnverified"].GetProperty("salesAmount").GetDecimal());
        Assert.Equal(2, channels["BankManual"].GetProperty("paymentCount").GetInt32());
        Assert.Equal(15m, overview.GetProperty("cashDifferenceTotal").GetDecimal());
        Assert.Equal(detail.GetRawText(), dashboard.GetProperty("employeeStats")[0].GetProperty("paymentBreakdown").GetRawText());
        Assert.Equal(detail.GetRawText(), dashboard.GetProperty("shifts")[0].GetProperty("paymentBreakdown").GetRawText());
        var evidence = await admin.JsonAsync(HttpMethod.Get, $"/admin/pos-shift/reconciliation/data?shiftId={shiftId}");
        Assert.Equal(detail.GetRawText(), evidence.GetProperty("paymentBreakdown").GetRawText());
        Assert.Equal(155m, evidence.GetProperty("detailExpectedCash").GetDecimal());
        Assert.Equal(2, evidence.GetProperty("cashTransactions").GetArrayLength());
        Assert.Single(evidence.GetProperty("refunds").EnumerateArray());
        using (var response = await admin.Http.GetAsync("/admin/pos/shift/manager-dashboard/export-excel"))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
            using var workbook = new ClosedXML.Excel.XLWorkbook(stream);
            var sheet = workbook.Worksheet("Chi tiết thanh toán");
            Assert.Equal("CK thủ công", sheet.Cell(2, 3).GetString());
            Assert.Equal(45m, sheet.Cell(2, 6).GetValue<decimal>());
            Assert.Equal(5m, sheet.Cell(2, 7).GetValue<decimal>());
        }
        using (var denied = await cashier.Http.GetAsync("/admin/pos/shift/manager-dashboard")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        var empty = await foreign.JsonAsync(HttpMethod.Get, "/admin/pos/shift/manager-dashboard");
        Assert.Equal(0m, empty.GetProperty("overview").GetProperty("paymentBreakdown").GetProperty("detailNonCashSalesTotal").GetDecimal());
        using (var denied = await foreign.Http.GetAsync($"/admin/pos-shift/reconciliation/data?shiftId={shiftId}")) Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(155m, (await final.POSShifts.SingleAsync()).ClosingCashExpected);
        Assert.Equal(170m, (await final.POSShifts.SingleAsync()).ClosingCashActual);
        Assert.Equal(2, await final.POSShiftCashTransactions.CountAsync());
    }
}
