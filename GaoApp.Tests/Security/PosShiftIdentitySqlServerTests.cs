using System.Net.Http.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosShiftIdentitySqlServerTests
{
    [Fact]
    public async Task Current_shift_and_historical_order_show_employee_and_terminal_instead_of_viewer_identity()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var cashier = await app.AddAccountAsync(store, "*");
        var viewer = await app.AddAccountAsync(store, "*");
        int orderId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Users.SingleAsync(x => x.Id == cashier.UserId)).FullName = "Nguyễn Thu Ngân";
            (await db.POSTerminals.SingleAsync(x => x.Id == store.TerminalId)).Name = "Máy tính tiền 02";
            await db.SaveChangesAsync();
        }
        using var owner = await app.LoginAsync(cashier);
        var opened = await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var current = await owner.JsonAsync(HttpMethod.Get, "/admin/pos/shift/current");
        Assert.Equal("Nguyễn Thu Ngân", current.GetProperty("openedByUserName").GetString());
        Assert.Equal("Máy tính tiền 02", current.GetProperty("terminalName").GetString());
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = new Order { StoreId = store.StoreId, POSShiftId = opened.GetProperty("id").GetInt32(),
                Status = OrderStatus.Completed, PaymentStatus = PaymentStatus.Paid, CompletedAtUtc = DateTime.UtcNow };
            db.Orders.Add(order); await db.SaveChangesAsync(); orderId = order.Id;
        }
        await owner.JsonAsync(HttpMethod.Post, "/admin/pos/shift/close", new { closingCashActual = 0 });
        using var manager = await app.LoginAsync(viewer);
        var receipt = await manager.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{orderId}");
        Assert.Equal("Nguyễn Thu Ngân", receipt.GetProperty("cashierName").GetString());
        Assert.Equal("Máy tính tiền 02", receipt.GetProperty("terminalName").GetString());
        Assert.Equal(opened.GetProperty("shiftCode").GetString(), receipt.GetProperty("shiftCode").GetString());
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using var response = await foreign.Http.GetAsync($"/admin/pos/orders/{orderId}");
        Assert.False(response.IsSuccessStatusCode);
    }
}
