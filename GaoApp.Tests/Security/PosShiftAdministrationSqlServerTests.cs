using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftAdministrationSqlServerTests
{
    internal static async Task<FullApplicationFixture.Account> AddAdminAsync(
        FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var account = await app.AddAccountAsync(store, "*");
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var membership = await db.UserInStores.SingleAsync(x => x.UserId == account.UserId);
        membership.RoleId = await db.Roles.Where(x => x.Code == "ADMIN" && x.IsSystemRole).Select(x => x.Id).SingleAsync();
        await db.SaveChangesAsync();
        return account;
    }

    [Fact]
    public async Task Admin_assigns_terminal_and_records_the_actual_receiver_on_single_consumption()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var admin = await app.LoginAsync(await AddAdminAsync(app, store));
        var employeeAccount = await app.AddAccountAsync(store, "*"); // Even all permissions are not the ADMIN role.
        using var employee = await app.LoginAsync(employeeAccount);
        using var other = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var options = await admin.JsonAsync(HttpMethod.Get, "/admin/pos/shift-handover-slips/assignments");
        Assert.Contains(options.GetProperty("terminals").EnumerateArray(), x => x.GetProperty("id").GetInt32() == store.TerminalId);
        Assert.DoesNotContain(options.GetProperty("terminals").EnumerateArray(), x => x.GetProperty("id").GetInt32() == app.Stores[1].TerminalId);

        object Slip(int userId, int terminalId, int? warehouse = null) => new {
            assignedToUserId = userId, terminalId, warehouseId = warehouse ?? store.WarehouseId,
            denominations = new[] { new { denominationValue = 50000, quantity = 2 } }
        };
        foreach (var path in new[] { "/admin/pos/shift-handover-slips/assignments", "/admin/pos/shift-handover-slips", "/admin/pos/shift/manager-dashboard", "/admin/pos/shift/manager-dashboard/export-excel" })
            await Expect(employee, HttpMethod.Get, path, null, HttpStatusCode.Forbidden);
        await Expect(employee, HttpMethod.Post, "/admin/pos/shift-handover-slips", Slip(employeeAccount.UserId, store.TerminalId), HttpStatusCode.Forbidden);
        await Expect(admin, HttpMethod.Post, "/admin/pos/shift-handover-slips", Slip(employeeAccount.UserId, app.Stores[1].TerminalId), HttpStatusCode.BadRequest);
        await Expect(admin, HttpMethod.Post, "/admin/pos/shift-handover-slips", Slip(employeeAccount.UserId, store.TerminalId, app.Stores[1].WarehouseId), HttpStatusCode.BadRequest);

        var slip = await admin.JsonAsync(HttpMethod.Post, "/admin/pos/shift-handover-slips", Slip(employeeAccount.UserId, store.TerminalId));
        var id = slip.GetProperty("id").GetInt32();
        var barcode = slip.GetProperty("barcodeValue").GetString();
        var pathByCode = "/admin/pos/shift-handover-slips/barcode?barcodeValue=" + barcode;
        Assert.Equal(JsonValueKind.Null, slip.GetProperty("assignedToUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, slip.GetProperty("usedByUserId").ValueKind);
        await other.JsonAsync(HttpMethod.Get, pathByCode); // Any cashier at this terminal may receive the slip.
        await Expect(employee, HttpMethod.Get, $"/admin/pos/shift-handover-slips/{id}", null, HttpStatusCode.Forbidden);
        await Expect(employee, HttpMethod.Post, $"/admin/pos/shift-handover-slips/{id}/mark-printed", new { }, HttpStatusCode.Forbidden);
        await Expect(employee, HttpMethod.Post, $"/admin/pos/shift-handover-slips/{id}/cancel", new { reason = "Không được phép" }, HttpStatusCode.Forbidden);
        await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/shift-handover-slips/{id}/mark-printed", new { });
        Assert.Equal(100000m, (await employee.JsonAsync(HttpMethod.Get, pathByCode)).GetProperty("openingCashTotal").GetDecimal());

        int secondTerminal;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "E2E-02", Name = "Quầy khác" };
            db.POSTerminals.Add(terminal); await db.SaveChangesAsync(); secondTerminal = terminal.Id;
        }
        using var wrongTerminal = await app.LoginAsync(employeeAccount with { Store = store with { TerminalId = secondTerminal } });
        await Expect(wrongTerminal, HttpMethod.Get, pathByCode, null, HttpStatusCode.Forbidden);
        await Expect(wrongTerminal, HttpMethod.Post, "/admin/pos/shift/open", new { handoverSlipId = id }, HttpStatusCode.BadRequest);
        var shift = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { handoverSlipId = id, openingCash = 1, warehouseId = app.Stores[1].WarehouseId });
        Assert.Equal(100000m, shift.GetProperty("openingCash").GetDecimal());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var saved = await db.POSShiftHandoverSlips.SingleAsync(x => x.Id == id);
            Assert.Equal(POSShiftHandoverSlipStatus.Used, saved.Status);
            Assert.Equal(shift.GetProperty("id").GetInt32(), saved.UsedPOSShiftId);
            Assert.Equal(employeeAccount.UserId, saved.UsedByUserId);
        }
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 100000 });
        await Expect(employee, HttpMethod.Post, "/admin/pos/shift/open", new { handoverSlipId = id }, HttpStatusCode.BadRequest);
        await Expect(admin, HttpMethod.Post, $"/admin/pos/shift-handover-slips/{id}/cancel", new { reason = "Đã dùng" }, HttpStatusCode.Conflict);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await final.POSShifts.ToListAsync());
    }

    [Fact]
    public async Task Cash_receipt_preserves_employee_cash_requires_admin_and_cannot_be_overwritten()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var adminAccount = await AddAdminAsync(app, store);
        using var admin = await app.LoginAsync(adminAccount);
        using var foreignAdmin = await app.LoginAsync(await AddAdminAsync(app, app.Stores[1]));
        using var employee = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var shift = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100000, warehouseId = store.WarehouseId });
        var id = shift.GetProperty("id").GetInt32();
        var path = $"/admin/pos/shift/{id}/cash-receipt";
        await Expect(admin, HttpMethod.Post, path, new { receivedAmount = 100000 }, HttpStatusCode.Conflict);
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 99000, note = "Nhân viên khai thiếu 1.000" });
        await Expect(employee, HttpMethod.Post, path, new { receivedAmount = 99000 }, HttpStatusCode.Forbidden);
        await Expect(foreignAdmin, HttpMethod.Post, path, new { receivedAmount = 99000 }, HttpStatusCode.NotFound);
        foreach (var body in new object[] { new { }, new { receivedAmount = -1 }, new { receivedAmount = 0.5 }, new { receivedAmount = 98000 }, new { receivedAmount = 99000, note = new string('x', 501) } })
            await Expect(admin, HttpMethod.Post, path, body, HttpStatusCode.BadRequest);
        var receipt = new { receivedAmount = 98000, note = "Admin nhận thiếu 1.000 so với nhân viên" };
        await admin.JsonAsync(HttpMethod.Post, path, receipt);
        DateTime receivedAt;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var saved = await db.POSShifts.SingleAsync(x => x.Id == id);
            Assert.Equal(99000m, saved.ClosingCashActual);
            Assert.Equal(100000m, saved.ClosingCashExpected);
            Assert.Equal(98000m, saved.CashReceivedAmount);
            Assert.Equal(adminAccount.UserId, saved.CashReceivedByUserId);
            Assert.Equal(receipt.note, saved.CashReceiptNote);
            receivedAt = saved.CashReceivedAtUtc!.Value;
            Assert.Equal(99000m, (await db.POSShiftClosingSlips.SingleAsync()).ClosingCashActual);
        }
        await admin.JsonAsync(HttpMethod.Post, path, receipt); // Transport retry, no new receipt.
        await Expect(admin, HttpMethod.Post, path, new { receivedAmount = 99000, note = "Ghi đè" }, HttpStatusCode.Conflict);
        var dashboard = await admin.JsonAsync(HttpMethod.Get, "/admin/pos/shift/manager-dashboard");
        var row = Assert.Single(dashboard.GetProperty("shifts").EnumerateArray());
        Assert.Equal(98000m, row.GetProperty("cashReceivedAmount").GetDecimal());
        Assert.Equal(adminAccount.Name, row.GetProperty("cashReceivedByUserName").GetString());
        using var excel = await admin.Http.GetAsync("/admin/pos/shift/manager-dashboard/export-excel");
        Assert.Equal(HttpStatusCode.OK, excel.StatusCode);
        await using var final = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(receivedAt, (await final.POSShifts.SingleAsync()).CashReceivedAtUtc);

        // Competing confirmations must yield one winner, never overwrite one another.
        var next = await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        await employee.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
        var nextId = next.GetProperty("id").GetInt32();
        var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(i => admin.Http.PostAsJsonAsync(
            $"/admin/pos/shift/{nextId}/cash-receipt", new { receivedAmount = 0, note = $"Xác nhận cạnh tranh {i}" })));
        try
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in results) response.Dispose(); }

        // Permission is checked against live membership, not stale login claims.
        var membership = await final.UserInStores.SingleAsync(x => x.UserId == adminAccount.UserId);
        membership.IsActive = false; await final.SaveChangesAsync();
        using var revoked = await admin.Http.GetAsync("/admin/pos/shift/manager-dashboard");
        Assert.False(revoked.IsSuccessStatusCode);
    }

    private static async Task Expect(FullApplicationFixture.Client client, HttpMethod method, string path, object? body, HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body != null) request.Content = JsonContent.Create(body);
        using var response = await client.Http.SendAsync(request);
        Assert.True(response.StatusCode == status, $"{method} {path}: expected {(int)status}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
