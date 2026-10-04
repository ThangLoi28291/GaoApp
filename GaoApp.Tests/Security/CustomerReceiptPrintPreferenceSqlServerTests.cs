using System.Net;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerReceiptPrintPreferenceSqlServerTests
{
    [Fact]
    public async Task Customer_preference_persists_flows_to_offline_and_uses_paid_order_not_next_cart()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        int customerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách không lấy bill", Phone = "0901122334" };
            db.Customers.Add(customer); await db.SaveChangesAsync(); customerId = customer.Id;
            Assert.False(customer.AskBeforePrintingReceipt);
        }
        var edit = await client.Http.GetStringAsync($"/Admin/Customer/Edit/{customerId}");
        Assert.Contains("AskBeforePrintingReceipt", edit);
        async Task SetPreference(bool value)
        {
            await using var db = app.Database.CreateTenantContext(store.StoreId);
            var customer = await db.Customers.SingleAsync(x => x.Id == customerId);
            using var saved = await client.Http.PostAsync("/Admin/Customer/Edit", new FormUrlEncodedContent(new Dictionary<string, string> {
                ["Id"] = customerId.ToString(), ["Name"] = customer.Name, ["Phone"] = customer.Phone!, ["PriceTier"] = "RETAIL", ["IsActive"] = "true",
                ["RowVersion"] = Convert.ToBase64String(customer.RowVersion), ["AskBeforePrintingReceipt"] = value.ToString()
            }));
            Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
            await db.Entry(customer).ReloadAsync(); Assert.Equal(value, customer.AskBeforePrintingReceipt);
        }
        await SetPreference(true);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var customers = await client.JsonAsync(HttpMethod.Get, "/admin/pos/offline/customers");
        Assert.True(customers.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("customerId").GetInt32() == customerId).GetProperty("askBeforePrintingReceipt").GetBoolean());
        var first = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        int id = first.GetProperty("orderId").GetInt32();
        var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{customerId}", new { repriceExistingLines = false });
        Assert.True(draft.GetProperty("askBeforePrintingReceipt").GetBoolean());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=1", new { });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", new { orderId = id, clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        var next = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var nextScreen = await client.JsonAsync(HttpMethod.Get, "/admin/pos/screen");
        Assert.False(nextScreen.GetProperty("currentDraft").GetProperty("askBeforePrintingReceipt").GetBoolean());
        var intent = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/invoice-route", new { route = 1 });
        Assert.True(intent.GetProperty("askBeforePrintingReceipt").GetBoolean());
        Assert.Equal(id, intent.GetProperty("orderId").GetInt32());
        var nextId = next.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{nextId}/items?variantId={store.VariantId}&qty=1", new { });
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", new { orderId = nextId, clientRequestId = Guid.NewGuid(), method = 0, amount = 20 });
        var nextIntent = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{nextId}/invoice-route", new { route = 1 });
        Assert.False(nextIntent.GetProperty("askBeforePrintingReceipt").GetBoolean());
        await SetPreference(false);
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.OrderPayments.Where(x => x.OrderId == id).ToListAsync());
    }
}
