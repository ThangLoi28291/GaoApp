using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosPendingQrSqlServerTests
{
    [Fact]
    public async Task Concurrent_HTTP_create_clicks_keep_one_QR_and_conflict_reopens_it_until_confirmation_or_cancellation()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.Add(new StoreBankAccount { StoreId = store.StoreId, BankCode = "ACB", BankName = "Test bank",
                AccountNumber = "123456789", AccountName = "Synthetic POS", VietQrBankBin = "970416",
                IsActive = true, IsDefault = true, QrRenderMode = BankQrRenderMode.LocalEmvQr, ConfirmMode = BankQrConfirmMode.Manual });
            await db.SaveChangesAsync();
        }
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var order = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var orderId = order.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=10");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.Http.PostAsJsonAsync("/admin/pos/cart/current/payment-qr", new { clientRequestId = Guid.NewGuid(), amount = 1 })));
        try
        {
            var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            var qr = await created.Content.ReadFromJsonAsync<JsonElement>();
            var qrId = qr.GetProperty("id").GetInt32();
            foreach (var response in responses.Where(response => response != created))
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                var conflict = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal("POS_QR_PENDING", conflict.GetProperty("errorCode").GetString());
                var metadata = conflict.GetProperty("metadata");
                Assert.Equal(orderId, metadata.GetProperty("orderId").GetInt32());
                Assert.Equal(qrId, metadata.GetProperty("savedQr").GetProperty("qr").GetProperty("id").GetInt32());
                Assert.Equal(qr.GetProperty("qrDataUrl").GetString(), metadata.GetProperty("savedQr").GetProperty("qr").GetProperty("qrDataUrl").GetString());
            }
            await using (var verify = app.Database.CreateTenantContext(store.StoreId))
            {
                Assert.Single(await verify.PosPaymentQrRequests.Where(q => q.OrderId == orderId).ToListAsync());
                Assert.Empty(await verify.OrderPayments.Where(p => p.OrderId == orderId).ToListAsync());
            }
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/payment-qr/{qrId}/manual-confirm", new { });
            var next = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { clientRequestId = Guid.NewGuid(), amount = 1 });
            var nextId = next.GetProperty("id").GetInt32();
            Assert.NotEqual(qrId, nextId);
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/payment-qr/{nextId}/cancel", new { });
            var last = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-qr", new { clientRequestId = Guid.NewGuid(), amount = 1 });
            Assert.NotEqual(nextId, last.GetProperty("id").GetInt32());
            await using var final = app.Database.CreateTenantContext(store.StoreId);
            Assert.Equal(3, await final.PosPaymentQrRequests.CountAsync(q => q.OrderId == orderId));
            Assert.Equal(1, await final.PosPaymentQrRequests.CountAsync(q => q.OrderId == orderId && q.Status == PosPaymentQrStatus.Pending));
            Assert.Single(await final.OrderPayments.Where(p => p.OrderId == orderId).ToListAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }
}
