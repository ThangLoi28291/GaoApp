using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSDepositPaymentAdjustmentSqlServerTests
{
    private const string Base = "/admin/pos-shift/payment-adjustments";
    private static object Input(JsonElement row, int method, Guid? key = null) => new
    { clientRequestId = key ?? Guid.NewGuid(), rowVersion = row.GetProperty("rowVersion").GetString(), method, requestReason = "Nhập nhầm phương thức nhận cọc, đã kiểm tra thực nhận" };
    private static object Decision(JsonElement detail, string? note = null) => new
    { rowVersion = detail.GetProperty("request").GetProperty("rowVersion").GetString(), shiftRowVersion = detail.GetProperty("shiftRowVersion").GetString(), note };
    private static Task<JsonElement> Detail(FullApplicationFixture.Client c, int id) => c.JsonAsync(HttpMethod.Get, Base + $"/{id}");
    private static async Task<JsonElement> Candidate(FullApplicationFixture.Client c, int id) =>
        (await c.JsonAsync(HttpMethod.Get, Base + $"/deposits?entryId={id}")).GetProperty("items")[0];
    private static async Task<int> Send(FullApplicationFixture.Client c, int id, object input) =>
        (await c.JsonAsync(HttpMethod.Post, Base + $"/deposits/{id}/requests", input)).GetProperty("id").GetInt32();
    private static async Task Expect(FullApplicationFixture.Client c, string path, object input, HttpStatusCode status)
    { using var r = await c.Http.PostAsJsonAsync(path, input); Assert.True(r.StatusCode == status, $"{path}: {await r.Content.ReadAsStringAsync()}"); }

    private static async Task<(int Customer, int Bank)> Setup(FullApplicationFixture app)
    {
        var s = app.Stores[0]; await using var db = app.Database.CreateTenantContext(s.StoreId);
        var c = new Customer { StoreId = s.StoreId, Name = "Khách cọc cần đối chiếu" }; db.Customers.Add(c);
        var bank = new StoreBankAccount { StoreId = s.StoreId, BankName = "Test", BankCode = "ACB", AccountNumber = "DEPOSIT-TEST",
            AccountName = "Test", VietQrBankBin = "970416", IsActive = true, IsDefault = true, ConfirmMode = BankQrConfirmMode.Manual };
        db.StoreBankAccounts.Add(bank); await db.SaveChangesAsync(); return (c.Id, bank.Id);
    }

    [Fact]
    public async Task Closed_received_deposit_can_change_both_ways_without_reference_or_changing_used_balance_refund_or_closing_evidence()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var s = app.Stores[0]; var cashier = await app.AddAccountAsync(s, "*");
        using var employee = await app.LoginAsync(cashier); using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, s));
        using var peer = await app.LoginAsync(await app.AddAccountAsync(s, "*")); using var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        var (customer, _) = await Setup(app);
        var shiftId = (await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100, warehouseId = s.WarehouseId })).GetProperty("id").GetInt32();
        var receive = new { clientRequestId = Guid.NewGuid(), customerId = customer, amount = 100, method = 0, purpose = "Cọc đặt hàng" };
        var deposit = (await employee.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", receive)).GetProperty("depositId").GetInt32();
        var order = (await employee.JsonAsync(HttpMethod.Post, "/admin/pos/draft")).GetProperty("orderId").GetInt32();
        await employee.JsonAsync(HttpMethod.Post, $"/admin/pos/{order}/items?variantId={s.VariantId}&qty=3");
        await employee.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customer}", new { repriceExistingLines = false });
        await employee.JsonAsync(HttpMethod.Post, $"/admin/customer-deposit/orders/{order}/select", new { expectedCustomerId = customer, depositId = deposit, amount = 60 });
        await employee.JsonAsync(HttpMethod.Post, $"/admin/pos/{order}/finalize");
        await employee.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/refund", new { clientRequestId = Guid.NewGuid(), depositId = deposit, amount = 10, method = 0, note = "Hoàn một phần cọc còn lại" });
        int entryId, cashId; DateTime receivedAt; string? originalPayload;
        await using (var db = app.Database.CreateTenantContext(s.StoreId))
        {
            var entry = await db.Set<CustomerDepositEntry>().SingleAsync(x => x.Kind == "Receive"); entryId = entry.Id; receivedAt = entry.CreatedAtUtc; originalPayload = entry.RequestJson;
            cashId = (await db.POSShiftCashTransactions.SingleAsync(x => x.CustomerDepositEntryId == entryId)).Id;
        }
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 190, note = "Đếm gốc" });
        await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/shift/{shiftId}/cash-receipt", new { receivedAmount = 189, note = "Bàn giao gốc" });
        var candidate = await Candidate(employee, entryId); Assert.True(candidate.GetProperty("canRequest").GetBoolean());
        Assert.Equal(customer, candidate.GetProperty("customerId").GetInt32());
        var body = Input(candidate, 1); var request = await Send(employee, entryId, body); Assert.Equal(request, await Send(employee, entryId, body));
        await Expect(peer, Base + $"/deposits/{entryId}/requests", body, HttpStatusCode.Forbidden);
        Assert.Empty((await peer.JsonAsync(HttpMethod.Get, Base + "/deposits")).GetProperty("items").EnumerateArray());
        using (var r = await peer.Http.GetAsync(Base + $"/{request}")) Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        using (var r = await foreign.Http.GetAsync(Base + $"/{request}")) Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        await Expect(employee, Base + $"/deposits/{entryId}/requests", Input(candidate, 1), HttpStatusCode.Conflict);
        var d = await Detail(admin, request); Assert.Equal(-100m, d.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        Assert.Equal(90m, d.GetProperty("proposedShift").GetProperty("expected").GetDecimal());
        await Expect(employee, Base + $"/{request}/approve", Decision(d), HttpStatusCode.Forbidden);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.Http.PostAsJsonAsync(Base + $"/{request}/approve", Decision(d))));
        foreach (var result in results) { using (result) Assert.True(result.IsSuccessStatusCode, await result.Content.ReadAsStringAsync()); }
        Assert.Equal(request, await Send(employee, entryId, body));
        Assert.Equal(deposit, (await employee.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", receive)).GetProperty("depositId").GetInt32());
        await using (var db = app.Database.CreateTenantContext(s.StoreId))
        {
            Assert.Equal(30m, (await db.Set<CustomerDeposit>().SingleAsync()).Balance);
            var entry = await db.Set<CustomerDepositEntry>().SingleAsync(x => x.Id == entryId);
            Assert.Equal(PaymentMethod.BankTransfer, entry.Method); Assert.Null(entry.Reference); Assert.Equal(originalPayload, entry.RequestJson); Assert.Equal(receivedAt, entry.CreatedAtUtc);
            Assert.True((await db.POSShiftCashTransactions.IgnoreQueryFilters().SingleAsync(x => x.Id == cashId)).IsDeleted);
        }
        var back = await Send(employee, entryId, Input(await Candidate(employee, entryId), 0));
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{back}/approve", Decision(await Detail(admin, back)));
        var reconciled = await Detail(admin, back);
        await admin.JsonAsync(HttpMethod.Post, Base + $"/shifts/{shiftId}/reconcile", new { rowVersion = reconciled.GetProperty("shiftRowVersion").GetString(), note = "Đã dò khoản cọc và quỹ" });
        await using (var db = app.Database.CreateTenantContext(s.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync(); Assert.Equal(100m, shift.CashInTotal); Assert.Equal(10m, shift.CashOutTotal); Assert.Equal(190m, shift.ClosingCashExpected);
            Assert.Equal(190m, shift.ClosingCashActual); Assert.Equal(189m, shift.CashReceivedAmount); Assert.False(shift.NeedsCashReconciliation);
            var slip = await db.POSShiftClosingSlips.SingleAsync(); Assert.Equal(190m, slip.ClosingCashExpected); Assert.Equal(100m, slip.CashInTotal);
            Assert.Equal(30m, (await db.Set<CustomerDeposit>().SingleAsync()).Balance);
            Assert.Equal(60m, (await db.Orders.SingleAsync()).PaidTotal); Assert.Equal(60m, (await db.Orders.SingleAsync()).DepositAmount);
            Assert.Equal(-10m, (await db.Set<CustomerDepositEntry>().SingleAsync(x => x.Kind == "Refund")).Amount);
            Assert.Equal(cashId, (await db.POSShiftCashTransactions.SingleAsync(x => x.CustomerDepositEntryId == entryId)).Id);
            Assert.All(await db.Set<POSPaymentAdjustmentRequest>().ToListAsync(), x => Assert.NotNull(x.ReconciledAtUtc));
        }
        var recon = await employee.JsonAsync(HttpMethod.Get, $"/admin/pos-shift/reconciliation/data?shiftId={shiftId}");
        Assert.Equal(190m, recon.GetProperty("detailExpectedCash").GetDecimal()); Assert.Equal(2, recon.GetProperty("paymentAdjustments").GetArrayLength());
        Assert.True(recon.GetProperty("otherMovements").EnumerateArray().Single(x => x.GetProperty("kind").GetString() == "Deposit:Receive").GetProperty("canRequest").GetBoolean());
        Assert.All(recon.GetProperty("cashTransactions").EnumerateArray(), x => Assert.False(x.GetProperty("canRequest").GetBoolean()));
    }

    [Fact]
    public async Task Original_transfer_legacy_cash_and_changed_evidence_require_fresh_approval_and_cannot_use_generic_voucher_corrections()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var s = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(s, "*")); using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, s));
        var (customer, bankId) = await Setup(app);
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100, warehouseId = s.WarehouseId });
        await employee.JsonAsync(HttpMethod.Post, "/admin/customer-deposit/receive", new { clientRequestId = Guid.NewGuid(), customerId = customer, amount = 100, method = 1, purpose = "Nhận cọc", reference = "Gốc" });
        int entryId; await using (var db = app.Database.CreateTenantContext(s.StoreId)) entryId = (await db.Set<CustomerDepositEntry>().SingleAsync()).Id;
        var id = await Send(employee, entryId, Input(await Candidate(employee, entryId), 0)); var stale = await Detail(admin, id);
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/cash-transaction", new { type = 1, amount = 5, reason = "Thu thêm" });
        await Expect(admin, Base + $"/{id}/approve", Decision(stale), HttpStatusCode.Conflict);
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/approve", Decision(await Detail(admin, id)));
        int cashId; string cashVersion;
        await using (var db = app.Database.CreateTenantContext(s.StoreId))
        {
            var cash = await db.POSShiftCashTransactions.SingleAsync(x => x.CustomerDepositEntryId == entryId);
            cash.CustomerDepositEntryId = null; await db.SaveChangesAsync(); // Simulate a pre-upgrade receipt.
            cashId = cash.Id; cashVersion = Convert.ToBase64String(cash.RowVersion);
        }
        await Expect(employee, "/admin/pos-shift/cash-adjustments/transactions/" + cashId + "/requests", new { clientRequestId = Guid.NewGuid(), rowVersion = cashVersion, type = 1, amount = 90, reason = "Sửa", requestReason = "Nhầm" }, HttpStatusCode.Conflict);
        var change = await Send(employee, entryId, Input(await Candidate(employee, entryId), 1));
        await using (var db = app.Database.CreateTenantContext(s.StoreId)) { (await db.Set<CustomerDepositEntry>().SingleAsync()).Note = "Đã đổi chứng từ"; await db.SaveChangesAsync(); }
        Assert.False((await Detail(admin, change)).GetProperty("canApprove").GetBoolean());
        await Expect(admin, Base + $"/{change}/approve", Decision(await Detail(admin, change)), HttpStatusCode.Conflict);
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{change}/reject", Decision(await Detail(admin, change), "Cần lập lại theo chứng từ mới"));
        var fresh = await Send(employee, entryId, Input(await Candidate(employee, entryId), 1));
        await using (var db = app.Database.CreateTenantContext(s.StoreId)) { (await db.StoreBankAccounts.SingleAsync(x => x.Id == bankId)).ConfirmMode = BankQrConfirmMode.Callback; await db.SaveChangesAsync(); }
        Assert.False((await Detail(admin, fresh)).GetProperty("canApprove").GetBoolean());
        await Expect(admin, Base + $"/{fresh}/approve", Decision(await Detail(admin, fresh)), HttpStatusCode.Conflict);
        await employee.JsonAsync(HttpMethod.Post, Base + $"/{fresh}/withdraw", Decision(await Detail(employee, fresh)));
        await using var verify = app.Database.CreateTenantContext(s.StoreId);
        Assert.Equal(100m, (await verify.Set<CustomerDeposit>().SingleAsync()).Balance);
        Assert.Equal(205m, (await verify.POSShifts.SingleAsync()).ClosingCashExpected);
        Assert.Equal(PaymentMethod.Cash, (await verify.Set<CustomerDepositEntry>().SingleAsync()).Method);
    }
}
