using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosReceiptPrintTransportTests
{
    [Fact]
    public async Task Changing_pairing_requires_reauthentication_before_printer_preferences_follow_the_new_terminal()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var account = await app.AddAccountAsync(store, "*");
        using var cashier = await app.LoginAsync(account);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var terminal = new POSTerminal { StoreId = store.StoreId, Code = "PRINT-02", Name = "Printer terminal 02" };
        db.POSTerminals.Add(terminal);
        await db.SaveChangesAsync();
        var key = Guid.NewGuid().ToString("N");
        db.POSTerminalDevices.Add(new POSTerminalDevice { StoreId = store.StoreId, TerminalId = terminal.Id, DeviceKey = key });
        await db.SaveChangesAsync();
        // A device binding cannot be swapped underneath an existing authenticated ticket.
        cashier.Cookies.Add(app.Address, new Cookie("POS_DEVICE_KEY", key, "/"));
        using (var changed = await cashier.Http.GetAsync("/admin/pos"))
            Assert.Equal(HttpStatusCode.Unauthorized, changed.StatusCode);

        // A fresh login on the new terminal obtains its own valid binding.
        using var paired = await app.LoginAsync(account with { Store = store with { TerminalId = terminal.Id } });
        await paired.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });

        var pos = await paired.Http.GetStringAsync("/admin/pos");
        Assert.Contains($"data-store-id=\"{store.StoreId}\"", pos);
        Assert.Contains($"data-terminal-id=\"{terminal.Id}\"", pos);
        var shift = await paired.Http.GetStringAsync("/admin/pos-shift");
        using var context = JsonDocument.Parse(Regex.Match(shift, "<script id=\"shiftPrintContext\"[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);
        Assert.Equal(store.StoreId, context.RootElement.GetProperty("storeId").GetInt32());
        Assert.Equal(terminal.Id, context.RootElement.GetProperty("terminalId").GetInt32());
    }

    [Fact]
    public async Task Paid_receipt_transport_uses_canonical_completed_cash_order_and_current_terminal_after_cart_switch()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var cashier = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var cart = await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var id = cart.GetProperty("orderId").GetInt32();
        await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=1", new { });
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize",
            new { orderId = id, clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        await cashier.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/invoice-route", new { route = 1 });
        await cashier.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });

        var model = await cashier.JsonAsync(HttpMethod.Get, $"/admin/pos/receipt/{id}?data=true");
        var receipt = model.GetProperty("receipt");
        Assert.Equal(id, receipt.GetProperty("orderId").GetInt32());
        Assert.Equal("Completed", receipt.GetProperty("status").GetString());
        var cash = Assert.Single(receipt.GetProperty("payments").EnumerateArray());
        Assert.Equal("Cash", cash.GetProperty("method").GetString());
        Assert.Equal(20m, cash.GetProperty("amount").GetDecimal());
        Assert.Equal(store.StoreId, model.GetProperty("storeId").GetInt32());
        Assert.Equal(store.TerminalId, model.GetProperty("terminalId").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(model.GetProperty("receiptDefault").GetProperty("template").GetProperty("design").GetProperty("name").GetString()));

        var html = await cashier.Http.GetStringAsync($"/admin/pos/receipt/{id}");
        using var embedded = JsonDocument.Parse(Regex.Match(html, "<script id=\"receiptPrintData\"[^>]*>(.*?)</script>", RegexOptions.Singleline).Groups[1].Value);
        Assert.Equal(receipt.GetRawText(), embedded.RootElement.GetProperty("receipt").GetRawText());
        using var viewOnly = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        using var denied = await viewOnly.Http.GetAsync($"/admin/pos/receipt/{id}?data=true");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }
}
