using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftHandoverEditSqlServerTests
{
    [Fact]
    public async Task Editing_unreceived_slips_rotates_barcode_and_rejects_stale_copies_and_concurrent_writes()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var admin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, store));
        using var foreignAdmin = await app.LoginAsync(await PosShiftAdministrationSqlServerTests.AddAdminAsync(app, app.Stores[1]));
        var cashierAccount = await app.AddAccountAsync(store, "*");
        using var cashier = await app.LoginAsync(cashierAccount);
        var create = new { terminalId = store.TerminalId, warehouseId = store.WarehouseId,
            denominations = new[] { new { denominationValue = 50000, quantity = 2 } } };
        var original = await admin.JsonAsync(HttpMethod.Post, "/admin/pos/shift-handover-slips", create);
        var id = original.GetProperty("id").GetInt32();
        var editUrl = $"/admin/pos/shift-handover-slips/{id}/update";
        object Edit(JsonElement slip, int quantity, int? terminalId = null, string? note = null) => new {
            terminalId = terminalId ?? store.TerminalId, warehouseId = store.WarehouseId,
            rowVersion = slip.GetProperty("rowVersion").GetString(), note,
            denominations = new[] { new { denominationValue = 50000, quantity } }
        };
        await Expect(cashier, editUrl, Edit(original, 3), HttpStatusCode.Forbidden);
        await Expect(foreignAdmin, editUrl, Edit(original, 3), HttpStatusCode.NotFound);
        await Expect(admin, editUrl, Edit(original, 3, app.Stores[1].TerminalId), HttpStatusCode.BadRequest);
        var edited = await admin.JsonAsync(HttpMethod.Post, editUrl, Edit(original, 3, note: "Sửa tiền đầu ca"));
        Assert.Equal(150000m, edited.GetProperty("openingCashTotal").GetDecimal());
        Assert.Equal(original.GetProperty("slipCode").GetString(), edited.GetProperty("slipCode").GetString());
        Assert.NotEqual(original.GetProperty("barcodeValue").GetString(), edited.GetProperty("barcodeValue").GetString());
        Assert.True(edited.GetProperty("requiresReprint").GetBoolean());
        Assert.Equal(JsonValueKind.Null, edited.GetProperty("printedAtUtc").ValueKind);
        using (var staleBarcode = await cashier.Http.GetAsync("/admin/pos/shift-handover-slips/barcode?barcodeValue=" + original.GetProperty("barcodeValue").GetString()))
            Assert.Equal(HttpStatusCode.NotFound, staleBarcode.StatusCode);
        await Expect(cashier, "/admin/pos/shift/open", new { handoverSlipId = id, handoverBarcodeValue = original.GetProperty("barcodeValue").GetString() }, HttpStatusCode.Conflict);
        await Expect(cashier, "/admin/pos/shift/open", new { handoverSlipId = id }, HttpStatusCode.Conflict);
        await Expect(admin, editUrl, Edit(original, 4), HttpStatusCode.Conflict);
        await Expect(admin, $"/admin/pos/shift-handover-slips/{id}/mark-printed?barcodeValue={original.GetProperty("barcodeValue").GetString()}", new { }, HttpStatusCode.Conflict);

        var printed = await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/shift-handover-slips/{id}/mark-printed?barcodeValue={edited.GetProperty("barcodeValue").GetString()}", new { });
        Assert.Equal((int)POSShiftHandoverSlipStatus.Printed, printed.GetProperty("status").GetInt32());
        var competing = await Task.WhenAll(new[] { 4, 5 }.Select(q => admin.Http.PostAsJsonAsync(editUrl, Edit(printed, q))));
        JsonElement winner;
        try
        {
            Assert.Single(competing, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(competing, x => x.StatusCode == HttpStatusCode.Conflict);
            winner = await competing.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<JsonElement>();
        }
        finally { foreach (var response in competing) response.Dispose(); }
        Assert.True(winner.GetProperty("requiresReprint").GetBoolean());
        var currentAmount = winner.GetProperty("openingCashTotal").GetDecimal();
        var opened = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { handoverSlipId = id, handoverBarcodeValue = winner.GetProperty("barcodeValue").GetString() });
        Assert.Equal(currentAmount, opened.GetProperty("openingCash").GetDecimal());
        await Expect(admin, editUrl, Edit(winner, 6), HttpStatusCode.Conflict);
        var used = await admin.JsonAsync(HttpMethod.Get, $"/admin/pos/shift-handover-slips/{id}");
        Assert.Equal(cashierAccount.UserId, used.GetProperty("usedByUserId").GetInt32());
        Assert.Equal(JsonValueKind.Null, used.GetProperty("assignedToUserId").ValueKind);
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = currentAmount });

        // Race between a cashier receiving the slip and admin editing it must be atomic.
        var raceSlip = await admin.JsonAsync(HttpMethod.Post, "/admin/pos/shift-handover-slips", create);
        var raceId = raceSlip.GetProperty("id").GetInt32();
        var race = await Task.WhenAll(
            admin.Http.PostAsJsonAsync($"/admin/pos/shift-handover-slips/{raceId}/update", Edit(raceSlip, 7)),
            cashier.Http.PostAsJsonAsync("/admin/pos/shift/open", new { handoverSlipId = raceId, handoverBarcodeValue = raceSlip.GetProperty("barcodeValue").GetString() }));
        try
        {
            Assert.Single(race, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Single(race, x => x.StatusCode == HttpStatusCode.Conflict);
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var saved = await db.POSShiftHandoverSlips.SingleAsync(x => x.Id == raceId);
            if (race[0].IsSuccessStatusCode)
            {
                Assert.Equal(POSShiftHandoverSlipStatus.Draft, saved.Status);
                Assert.Null(saved.UsedPOSShiftId);
                Assert.False(await db.POSShifts.AnyAsync(x => x.Status == POSShiftStatus.Open));
            }
            else
            {
                Assert.Equal(POSShiftHandoverSlipStatus.Used, saved.Status);
                Assert.Equal(100000m, (await db.POSShifts.SingleAsync(x => x.Status == POSShiftStatus.Open)).OpeningCash);
            }
        }
        finally { foreach (var response in race) response.Dispose(); }

        var cancelled = await admin.JsonAsync(HttpMethod.Post, "/admin/pos/shift-handover-slips", create);
        var cancelledId = cancelled.GetProperty("id").GetInt32();
        await admin.JsonAsync(HttpMethod.Post, $"/admin/pos/shift-handover-slips/{cancelledId}/cancel", new { reason = "Không dùng" });
        await Expect(admin, $"/admin/pos/shift-handover-slips/{cancelledId}/update", Edit(cancelled, 9), HttpStatusCode.Conflict);
    }

    private static async Task Expect(FullApplicationFixture.Client client, string path, object body, HttpStatusCode expected)
    {
        using var response = await client.Http.PostAsJsonAsync(path, body);
        Assert.True(response.StatusCode == expected, $"{path}: expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }
}
