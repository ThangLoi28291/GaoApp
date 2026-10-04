using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftCashDrawerSqlServerTests
{
    [Fact]
    public async Task Shift_count_drawer_uses_action_permissions_and_fixed_reasons_without_changing_shift_or_cash()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, PermissionCodes.Pos.Shift.View,
            PermissionCodes.Pos.Shift.Open, PermissionCodes.Pos.Shift.Close);
        using var owner = await app.LoginAsync(account);
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Shift.View));
        using var stranger = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        const string receiveUrl = "/admin/pos/shift/cash-drawer/receive";
        const string closeUrl = "/admin/pos/shift/cash-drawer/close";
        using (var response = await denied.Http.PostAsJsonAsync(receiveUrl, new { }))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var token = owner.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        owner.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await owner.Http.PostAsJsonAsync(receiveUrl, new { }))
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        owner.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var receive = await owner.JsonAsync(HttpMethod.Post, receiveUrl, new { reason = "Override attempted" });
        Assert.Equal("Receive", receive.GetProperty("purpose").GetString());
        Assert.Equal(JsonValueKind.Null, receive.GetProperty("shiftId").ValueKind);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.False(await db.POSShifts.AnyAsync());
        var shift = await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100000, warehouseId = store.WarehouseId });
        var id = shift.GetProperty("id").GetInt32();
        using (var response = await owner.Http.PostAsJsonAsync(receiveUrl, new { }))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var response = await stranger.Http.PostAsJsonAsync(receiveUrl, new { }))
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        foreach (var client in new[] { denied, stranger, foreign })
        {
            using var response = await client.Http.PostAsJsonAsync(closeUrl, new { shiftId = id });
            Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        }
        using (var response = await owner.Http.PostAsJsonAsync(closeUrl, new { shiftId = id + 10000 }))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var close = await owner.JsonAsync(HttpMethod.Post, closeUrl, new { shiftId = id, reason = "Override attempted" });
        Assert.Equal(id, close.GetProperty("shiftId").GetInt32());
        Assert.Equal("Close", close.GetProperty("purpose").GetString());
        var logs = await db.POSAuditLogs.Where(x => x.Action == "CASH_DRAWER_OPEN_REQUESTED").OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { "Mở két nhận ca", "Mở két chốt ca" }, logs.Select(x => x.Note));
        Assert.All(logs, log => { Assert.Equal(account.UserId, log.UserId); Assert.Equal(store.StoreId, log.StoreId); });
        var saved = await db.POSShifts.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(GaoApp.Domain.Enums.POSShiftStatus.Open, saved.Status);
        Assert.Equal(100000, saved.ClosingCashExpected);
        Assert.False(await db.Set<GaoApp.Domain.Entities.POSShiftCashTransaction>().AnyAsync());
        await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 100000 });
        using (var response = await owner.Http.PostAsJsonAsync(closeUrl, new { shiftId = id }))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Drawer_reason_requires_permission_current_shift_owner_and_antiforgery_and_does_not_move_money()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var owner = await app.LoginAsync(account);
        using var stranger = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Shift.View));
        using var anonymous = app.Anonymous(store);
        var shift = await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 100000, warehouseId = store.WarehouseId });
        var id = shift.GetProperty("id").GetInt32();
        var request = new { shiftId = id, reason = "  Đổi tiền lẻ cho khách  " };
        const string url = "/admin/pos/shift/cash-drawer";

        using (var response = await anonymous.Http.PostAsJsonAsync(url, request)) Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await denied.Http.PostAsJsonAsync(url, request)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await stranger.Http.PostAsJsonAsync(url, request)) Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        using (var response = await foreign.Http.PostAsJsonAsync(url, request)) Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        foreach (var reason in new[] { "", "   ", new string('a', 301) })
        {
            using var response = await owner.Http.PostAsJsonAsync(url, new { shiftId = id, reason });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using (var response = await owner.Http.PostAsJsonAsync(url, new { shiftId = id + 10000, reason = "Sai ca" }))
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var csrf = owner.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        owner.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var response = await owner.Http.PostAsJsonAsync(url, request)) Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        owner.Http.DefaultRequestHeaders.Add("RequestVerificationToken", csrf);

        var result = await owner.JsonAsync(HttpMethod.Post, url, request);
        Assert.Equal(id, result.GetProperty("shiftId").GetInt32());
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var log = await db.POSAuditLogs.SingleAsync(x => x.Action == "CASH_DRAWER_OPEN_REQUESTED");
        Assert.Equal(result.GetProperty("auditId").GetInt32(), log.Id);
        Assert.Equal(store.StoreId, log.StoreId); Assert.Equal(account.UserId, log.UserId);
        Assert.Equal("Đổi tiền lẻ cho khách", log.Note); Assert.Null(log.OrderId);
        using var metadata = JsonDocument.Parse(log.MetadataJson!);
        Assert.Equal(id, metadata.RootElement.GetProperty("ShiftId").GetInt32());
        Assert.Equal(store.TerminalId, metadata.RootElement.GetProperty("TerminalId").GetInt32());
        Assert.Equal("Requested", metadata.RootElement.GetProperty("Outcome").GetString());
        var saved = await db.POSShifts.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(100000, saved.ClosingCashExpected); Assert.Equal(0, saved.CashInTotal); Assert.Equal(0, saved.CashOutTotal);
        Assert.False(await db.Set<GaoApp.Domain.Entities.POSShiftCashTransaction>().AnyAsync());

        // Closing the shift revokes the action even if the page still has its old shift id.
        await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 100000 });
        using (var response = await owner.Http.PostAsJsonAsync(url, request)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await db.POSAuditLogs.CountAsync(x => x.Action == "CASH_DRAWER_OPEN_REQUESTED"));
    }
}
