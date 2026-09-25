using System.Net;
using System.Net.Http.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class CustomerDisplayInfoSqlServerTests
{
    [Fact]
    public async Task Wifi_migration_preserves_existing_store_identity_and_starts_hidden()
    {
        await using var database = new GaoApp.Tests.Configuration.InventoryPostingLocalDb();
        await database.MigrateAsync("20260911053655_AddReceiptIntakePacking");
        await database.ExecuteAsync("""
            INSERT dbo.Stores (Name, ReceiptName, SubDomain, SubDomainNormalized, IsActive, IsDeleted, CreatedAtUtc)
            VALUES (N'Tiệm gốc', N'Tiệm đang bán', N'wifi-upgrade', N'WIFI-UPGRADE', 1, 0, '2026-09-01');
            """);
        await database.MigrateAsync();
        await using var db = database.CreateHostContext();
        var store = await db.Stores.SingleAsync();
        Assert.Equal("Tiệm đang bán", store.ReceiptName);
        Assert.Equal("Tiệm gốc", store.Name);
        Assert.Null(store.GuestWifiName);
        Assert.Null(store.GuestWifiPassword);
    }

    [Fact]
    public async Task Display_uses_logged_in_operator_at_each_terminal_and_store_scoped_versioned_wifi()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var managerAccount = await app.AddAccountAsync(store, "*");
        int terminalId;
        string version;
        await using (var db = app.Database.CreateHostContext())
        {
            var terminal = new POSTerminal { StoreId = store.StoreId, Code = "DISPLAY02", Name = "Quầy 02" };
            db.POSTerminals.Add(terminal);
            (await db.Users.SingleAsync(x => x.Id == managerAccount.UserId)).FullName = "Nguyễn An";
            await db.SaveChangesAsync();
            terminalId = terminal.Id;
            version = Convert.ToBase64String((await db.Stores.SingleAsync(x => x.Id == store.StoreId)).RowVersion);
        }
        var secondAccount = await app.AddAccountAsync(store with { TerminalId = terminalId }, PermissionCodes.Pos.Order.View);
        await using (var db = app.Database.CreateHostContext())
        {
            (await db.Users.SingleAsync(x => x.Id == secondAccount.UserId)).FullName = "Trần Bình";
            await db.SaveChangesAsync();
        }
        using var manager = await app.LoginAsync(managerAccount);
        using var second = await app.LoginAsync(secondAccount);
        using var other = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], PermissionCodes.Pos.Order.View));
        const string infoUrl = "/admin/pos/customer-display/info", saveUrl = "/admin/displaypromotion/savewifi";
        var firstInfo = await manager.JsonAsync(HttpMethod.Get, infoUrl);
        var secondInfo = await second.JsonAsync(HttpMethod.Get, infoUrl);
        var otherInfo = await other.JsonAsync(HttpMethod.Get, infoUrl);
        Assert.Equal("Nguyễn An", firstInfo.GetProperty("cashierName").GetString());
        Assert.Equal("Trần Bình", secondInfo.GetProperty("cashierName").GetString());
        Assert.Equal(store.TerminalId, firstInfo.GetProperty("terminalId").GetInt32());
        Assert.Equal(terminalId, secondInfo.GetProperty("terminalId").GetInt32());
        Assert.Equal("Quầy 02", secondInfo.GetProperty("terminalName").GetString());
        Assert.Equal("", firstInfo.GetProperty("wifiName").GetString());
        var request = new SaveGuestWifiRequest { Name = " Gạo & Khách ", Password = " AbC<&>123 ", RowVersion = version };
        using (var denied = await second.Http.PutAsJsonAsync(saveUrl, request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await manager.Http.PutAsJsonAsync(saveUrl, request with { Name = new string('x', 129) })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await manager.Http.PutAsJsonAsync(saveUrl, request with { Name = "" })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var saved = await manager.JsonAsync(HttpMethod.Put, saveUrl, new { request.Name, request.Password, request.RowVersion, storeId = app.Stores[1].StoreId });
        foreach (var client in new[] { manager, second })
        {
            var info = await client.JsonAsync(HttpMethod.Get, infoUrl);
            Assert.Equal(request.Name, info.GetProperty("wifiName").GetString());
            Assert.Equal(request.Password, info.GetProperty("wifiPassword").GetString());
        }
        Assert.Equal(otherInfo.GetRawText(), (await other.JsonAsync(HttpMethod.Get, infoUrl)).GetRawText());
        using (var stale = await manager.Http.PutAsJsonAsync(saveUrl, request)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var next = request with { RowVersion = saved.GetProperty("rowVersion").GetString()! };
        var token = manager.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
        manager.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using (var csrf = await manager.Http.PutAsJsonAsync(saveUrl, next)) Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        manager.Http.DefaultRequestHeaders.Add("RequestVerificationToken", token);
        var openWifi = await manager.JsonAsync(HttpMethod.Put, saveUrl, next with { Password = "" });
        Assert.Equal("", (await second.JsonAsync(HttpMethod.Get, infoUrl)).GetProperty("wifiPassword").GetString());
        await manager.JsonAsync(HttpMethod.Put, saveUrl, next with { Name = "", Password = "", RowVersion = openWifi.GetProperty("rowVersion").GetString()! });
        Assert.Equal("", (await second.JsonAsync(HttpMethod.Get, infoUrl)).GetProperty("wifiName").GetString());

        // A later operator on the same device must replace the first operator, even with no current order.
        using var replacement = await app.LoginAsync(secondAccount with { Store = store });
        var replacementInfo = await replacement.JsonAsync(HttpMethod.Get, infoUrl);
        Assert.Equal("Trần Bình", replacementInfo.GetProperty("cashierName").GetString());
        Assert.Equal(store.TerminalId, replacementInfo.GetProperty("terminalId").GetInt32());
        await using var check = app.Database.CreateHostContext();
        Assert.Empty(await check.Orders.ToListAsync());
    }
}
