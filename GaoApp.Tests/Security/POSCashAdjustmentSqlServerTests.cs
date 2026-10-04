using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class POSCashAdjustmentSqlServerTests
{
    private const string Base = "/admin/pos-shift/cash-adjustments";
    private static async Task<JsonElement> Voucher(FullApplicationFixture.Client employee, int id) =>
        (await employee.JsonAsync(HttpMethod.Get, Base + $"/transactions?transactionId={id}")).GetProperty("items")[0];
    private static object Edit(JsonElement voucher, int type = 2, decimal amount = 10000, bool cancel = false, Guid? key = null) => new {
        clientRequestId = key ?? Guid.NewGuid(), rowVersion = voucher.GetProperty("rowVersion").GetString(), isCancellation = cancel,
        type, amount, reason = "Điều chỉnh nội dung", note = "Ghi chú mới", requestReason = "Nhập nhầm thu thành chi / số tiền"
    };
    private static async Task<int> Send(FullApplicationFixture.Client employee, int id, object body) =>
        (await employee.JsonAsync(HttpMethod.Post, Base + $"/transactions/{id}/requests", body)).GetProperty("id").GetInt32();
    private static async Task<JsonElement> Detail(FullApplicationFixture.Client client, int id) => await client.JsonAsync(HttpMethod.Get, Base + $"/{id}");
    private static object Decision(JsonElement detail, string? note = null) => new { rowVersion = detail.GetProperty("request").GetProperty("rowVersion").GetString(), note };
    private static async Task Expect(FullApplicationFixture.Client client, string path, object body, HttpStatusCode expected)
    {
        using var response = await client.Http.PostAsJsonAsync(path, body);
        Assert.True(response.StatusCode == expected, $"{path}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }
    private static async Task<(int Shift, int Voucher)> OpenAndCreate(FullApplicationFixture app, FullApplicationFixture.Client employee)
    {
        var shift = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100000, warehouseId = app.Stores[0].WarehouseId });
        var voucher = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/cash-transaction", new { type = 1, amount = 10000, reason = "Thu bổ sung", note = "Gốc" });
        return (shift.GetProperty("id").GetInt32(), voucher.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Requests_are_owner_scoped_and_admin_approval_applies_type_amount_cancel_exactly_once()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var employeeAccount = await app.AddAccountAsync(store, "*");
        using var employee = await app.LoginAsync(employeeAccount);
        using var other = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        using var foreign = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        var ids = await OpenAndCreate(app, employee); var voucher = await Voucher(employee, ids.Voucher);
        var body = Edit(voucher); var id = await Send(employee, ids.Voucher, body);
        Assert.Equal(id, await Send(employee, ids.Voucher, body));
        await Expect(employee, Base + $"/transactions/{ids.Voucher}/requests", Edit(voucher), HttpStatusCode.Conflict);
        await Expect(other, Base + $"/transactions/{ids.Voucher}/requests", body, HttpStatusCode.Forbidden);
        var detail = await Detail(admin, id);
        Assert.Equal(-20000m, detail.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        Assert.Equal(110000m, detail.GetProperty("currentShift").GetProperty("expected").GetDecimal());
        Assert.Equal(90000m, detail.GetProperty("proposedShift").GetProperty("expected").GetDecimal());
        var decision = Decision(detail);
        await Expect(employee, Base + $"/{id}/approve", decision, HttpStatusCode.Forbidden);
        await Expect(foreign, Base + $"/{id}/approve", decision, HttpStatusCode.NotFound);
        using (var inaccessible = await other.Http.GetAsync(Base + $"/{id}")) Assert.Equal(HttpStatusCode.Forbidden, inaccessible.StatusCode);
        Assert.Empty((await other.JsonAsync(HttpMethod.Get, Base + "/data")).GetProperty("items").EnumerateArray());
        Assert.Equal(id, (await employee.JsonAsync(HttpMethod.Get, "/admin/pos/shift/cash-transactions"))[0].GetProperty("pendingAdjustmentId").GetInt32());
        // Simultaneous approval/retry must apply the signed delta once.
        var responses = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => admin.Http.PostAsJsonAsync(Base + $"/{id}/approve", decision)));
        foreach (var response in responses) { using (response) Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); }
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/approve", decision);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var shift = await db.POSShifts.SingleAsync(x => x.Id == ids.Shift);
            Assert.Equal(0m, shift.CashInTotal); Assert.Equal(10000m, shift.CashOutTotal); Assert.Equal(90000m, shift.ClosingCashExpected);
            Assert.False(shift.NeedsCashReconciliation);
            Assert.Equal(POSShiftCashTransactionType.CashOut, (await db.Set<POSShiftCashTransaction>().SingleAsync()).Type);
        }
        // A further request can change amount; old versions cannot approve it.
        voucher = await Voucher(employee, ids.Voucher);
        var second = await Send(employee, ids.Voucher, Edit(voucher, amount: 15000));
        await Expect(admin, Base + $"/{second}/approve", new { rowVersion = "stale" }, HttpStatusCode.Conflict);
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{second}/approve", Decision(await Detail(admin, second)));
        Assert.Equal(85000m, (await Detail(admin, second)).GetProperty("currentShift").GetProperty("expected").GetDecimal());
        var cancel = await Send(employee, ids.Voucher, Edit(await Voucher(employee, ids.Voucher), cancel: true));
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{cancel}/approve", Decision(await Detail(admin, cancel)));
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100000m, (await final.POSShifts.SingleAsync()).ClosingCashExpected);
        var cancelled = await final.Set<POSShiftCashTransaction>().IgnoreQueryFilters().SingleAsync(x => x.Id == ids.Voucher);
        Assert.True(cancelled.IsDeleted); Assert.Equal(15000m, cancelled.Amount); Assert.NotNull(cancelled.DeletedAtUtc);
        Assert.Equal(3, await final.Set<POSCashAdjustmentRequest>().CountAsync());
        Assert.True((await Voucher(employee, ids.Voucher)).GetProperty("cancelled").GetBoolean());
    }

    [Fact]
    public async Task Closed_shift_keeps_actual_received_and_original_slip_and_requires_fresh_reconciliation()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var ids = await OpenAndCreate(app, employee);
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 109000, note = "Thiếu 1000" });
        await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/shift/{ids.Shift}/cash-receipt", new { receivedAmount = 108000, note = "Nhận thiếu 1000 so với thực đếm" });
        string slipBefore;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
            slipBefore = await db.POSShiftClosingSlips.Select(s => s.ClosingCashExpected + ":" + s.ClosingCashActual + ":" + s.CashInTotal + ":" + s.CashOutTotal).SingleAsync();
        var id = await Send(employee, ids.Voucher, Edit(await Voucher(employee, ids.Voucher)));
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/approve", Decision(await Detail(admin, id)));
        var detail = await Detail(admin, id); var p = detail.GetProperty("currentShift");
        Assert.Equal(90000m, p.GetProperty("expected").GetDecimal()); Assert.Equal(109000m, p.GetProperty("actual").GetDecimal());
        Assert.Equal(108000m, p.GetProperty("received").GetDecimal()); Assert.Equal(19000m, p.GetProperty("difference").GetDecimal());
        Assert.Equal(18000m, p.GetProperty("receivedDifference").GetDecimal()); Assert.True(p.GetProperty("needsReconciliation").GetBoolean());
        var reconcilePath = Base + $"/shifts/{ids.Shift}/reconcile";
        var oldVersion = detail.GetProperty("shiftRowVersion").GetString();
        await Expect(employee, reconcilePath, new { rowVersion = oldVersion, note = "Kiểm tra" }, HttpStatusCode.Forbidden);
        await Expect(admin, reconcilePath, new { rowVersion = oldVersion }, HttpStatusCode.BadRequest);
        await Expect(admin, reconcilePath, new { rowVersion = "stale", note = "Kiểm tra" }, HttpStatusCode.Conflict);
        await admin.JsonAsync(HttpMethod.Post, reconcilePath, new { rowVersion = oldVersion, note = "Đã kiểm tra chứng từ và chênh lệch sau điều chỉnh" });
        Assert.False((await Detail(admin, id)).GetProperty("currentShift").GetProperty("needsReconciliation").GetBoolean());
        var cancel = await Send(employee, ids.Voucher, Edit(await Voucher(employee, ids.Voucher), cancel: true));
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{cancel}/approve", Decision(await Detail(admin, cancel)));
        await Expect(admin, reconcilePath, new { rowVersion = oldVersion, note = "Màn hình cũ" }, HttpStatusCode.Conflict);
        Assert.Single((await admin.JsonAsync(HttpMethod.Get, Base + "/data?status=NeedsReconciliation")).GetProperty("items").EnumerateArray());
        Assert.True((await admin.JsonAsync(HttpMethod.Get, "/admin/pos/shift/manager-dashboard")).GetProperty("shifts")[0].GetProperty("needsCashReconciliation").GetBoolean());
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        var slipAfter = await final.POSShiftClosingSlips.Select(s => s.ClosingCashExpected + ":" + s.ClosingCashActual + ":" + s.CashInTotal + ":" + s.CashOutTotal).SingleAsync();
        Assert.Equal(slipBefore, slipAfter);
        var saved = await final.POSShifts.SingleAsync(); Assert.Equal(109000m, saved.ClosingCashActual); Assert.Equal(108000m, saved.CashReceivedAmount);
        Assert.Equal(100000m, saved.ClosingCashExpected); Assert.True(saved.NeedsCashReconciliation);
        var requests = await final.Set<POSCashAdjustmentRequest>().OrderBy(x => x.Id).ToListAsync();
        Assert.NotNull(requests[0].BeforeShiftJson); Assert.NotNull(requests[0].AfterShiftJson); Assert.NotNull(requests[0].ReconciledAtUtc);
        Assert.Null(requests[1].ReconciledAtUtc);
    }

    [Fact]
    public async Task Reject_withdraw_stale_voucher_and_csrf_do_not_change_cash()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var ids = await OpenAndCreate(app, employee); var voucher = await Voucher(employee, ids.Voucher);
        var id = await Send(employee, ids.Voucher, Edit(voucher));
        var decision = Decision(await Detail(admin, id));
        await Expect(admin, Base + $"/{id}/reject", decision, HttpStatusCode.BadRequest);
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/reject", Decision(await Detail(admin, id), "Chứng từ gốc đúng"));
        await Expect(admin, Base + $"/{id}/approve", decision, HttpStatusCode.Conflict);
        var withdrawn = await Send(employee, ids.Voucher, Edit(voucher));
        await employee.JsonAsync(HttpMethod.Post, Base + $"/{withdrawn}/withdraw", Decision(await Detail(employee, withdrawn)));
        var stale = await Send(employee, ids.Voucher, Edit(voucher));
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        { var row = await db.Set<POSShiftCashTransaction>().SingleAsync(); row.Note = "Đã chỉnh bởi tác vụ khác"; await db.SaveChangesAsync(); }
        Assert.False((await Detail(admin, stale)).GetProperty("canApprove").GetBoolean());
        await Expect(admin, Base + $"/{stale}/approve", Decision(await Detail(admin, stale)), HttpStatusCode.Conflict);
        admin.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        await Expect(admin, Base + $"/{stale}/reject", Decision(await Detail(admin, stale), "Tải lại"), HttpStatusCode.BadRequest);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(110000m, (await final.POSShifts.SingleAsync()).ClosingCashExpected);
        Assert.Equal(10000m, (await final.Set<POSShiftCashTransaction>().SingleAsync()).Amount);
        var requests = await final.Set<POSCashAdjustmentRequest>().OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { POSCashAdjustmentStatus.Rejected, POSCashAdjustmentStatus.Withdrawn, POSCashAdjustmentStatus.Pending }, requests.Select(x => x.Status));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_can_correct_30000_cash_in_to_35000_cash_out_and_preserve_negative_expected_cash(bool closed)
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        var shift = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var saved = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/cash-transaction", new { type = 1, amount = 30000, reason = "Nhập nhầm thu" });
        var voucherId = saved.GetProperty("id").GetInt32();
        // Preserve ordinary disbursement validation, independent of the admin correction workflow.
        await Expect(employee, "/admin/pos/shift/cash-transaction", new { type = 2, amount = 35000, reason = "Chi mới vượt quỹ" }, HttpStatusCode.BadRequest);
        if (closed) await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 30000 });
        var id = await Send(employee, voucherId, Edit(await Voucher(employee, voucherId), amount: 35000));
        var detail = await Detail(admin, id);
        Assert.Equal(30000m, detail.GetProperty("currentShift").GetProperty("expected").GetDecimal());
        Assert.Equal(-65000m, detail.GetProperty("request").GetProperty("expectedDelta").GetDecimal());
        Assert.Equal(-35000m, detail.GetProperty("proposedShift").GetProperty("expected").GetDecimal());
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/approve", Decision(detail));
        await admin.JsonAsync(HttpMethod.Post, Base + $"/{id}/approve", Decision(detail));
        if (!closed)
        {
            await Expect(employee, "/admin/pos/shift/close", new { closingCashActual = -35000 }, HttpStatusCode.BadRequest);
            await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0, note = "Tiền dự kiến âm, đang kiểm tra chứng từ" });
        }
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var current = await db.POSShifts.SingleAsync();
        Assert.Equal(-35000m, current.ClosingCashExpected); Assert.Equal(0m, current.CashInTotal); Assert.Equal(35000m, current.CashOutTotal);
        Assert.Equal(closed ? 30000m : 0m, current.ClosingCashActual);
        Assert.Equal(closed, current.NeedsCashReconciliation);
        var slip = await db.POSShiftClosingSlips.SingleAsync();
        Assert.Equal(closed ? 30000m : -35000m, slip.ClosingCashExpected);
        Assert.Equal(closed ? 30000m : 0m, slip.ClosingCashActual);
        Assert.Equal(POSCashAdjustmentStatus.Approved, (await db.Set<POSCashAdjustmentRequest>().SingleAsync()).Status);
    }
}
