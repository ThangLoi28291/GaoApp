using System.Net;
using System.Net.Http.Json;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PaymentCollectionSqlServerTests
{
    [Fact]
    public async Task Busy_collection_returns_retryable_conflict_and_same_identity_eventually_posts_once()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var orderId = await StartOrderAsync(client, store);
        var key = Guid.NewGuid();
        var body = new { orderId, clientRequestId = key, method = 0, amount = 20 };
        await using (var competingWriter = app.Database.CreateTenantContext(store.StoreId))
        await using (var transaction = await competingWriter.Database.BeginTransactionAsync())
        {
            var resource = $"pos-collection:{store.StoreId}:{key:N}";
            await competingWriter.Database.ExecuteSqlInterpolatedAsync($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=0; IF @result < 0 THROW 51000, 'Test lock was not acquired.', 1;");
            using var busy = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", body);
            Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);
            var conflict = await busy.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.Contains("Lần thu đang được xử lý", conflict.GetProperty("message").GetString());
            Assert.False(await competingWriter.OrderPayments.AnyAsync(x => x.OrderId == orderId));
            await transaction.RollbackAsync();
        }
        for (var attempt = 0; attempt < 2; attempt++)
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payments", body);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
        Assert.Equal(20m, await db.Orders.Where(x => x.Id == orderId).Select(x => x.PaidTotal).SingleAsync());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(50)]
    public async Task Concurrent_retries_post_one_collection_and_separate_equal_installments_remain_distinct(int count)
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var orderId = await StartOrderAsync(client, store);
        var key = Guid.NewGuid();
        var body = new { orderId, clientRequestId = key, method = 0, amount = 20 };
        var responses = await Task.WhenAll(Enumerable.Range(0, count).Select(_ =>
            client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", body)));
        foreach (var response in responses)
        {
            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    // The bounded SQL lock may ask a caller to retry under load.
                    // Exercise that contract with the SAME identity and body, after
                    // the initial concurrent requests finish; never create a new key.
                    var conflict = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
                    Assert.Contains("Lần thu đang được xử lý", conflict.GetProperty("message").GetString());
                    using var retry = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", body);
                    Assert.True(retry.StatusCode == HttpStatusCode.OK,
                        $"Same-identity retry failed: {retry.StatusCode} {await retry.Content.ReadAsStringAsync()}");
                }
                else Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"Concurrent retry failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }
        }
        using (var mismatch = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", new { orderId, clientRequestId = key, method = 0, amount = 21 }))
            Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.JsonAsync(HttpMethod.Post,
            $"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 20 })));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var order = await db.Orders.Include(x => x.Payments).SingleAsync(x => x.Id == orderId);
        Assert.Equal(60, order.PaidTotal);
        Assert.Equal(3, order.Payments.Count);
        Assert.All(order.Payments, p => Assert.Equal(20, p.Amount));
        Assert.Equal(100, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
        Assert.Equal(0, (await db.POSShifts.SingleAsync(x => x.Id == order.POSShiftId)).CashSalesTotal);
    }

    [Fact]
    public async Task Completed_retry_stays_on_original_order_and_deleted_keys_cannot_be_reused()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var orderId = await StartOrderAsync(client, store);
        var key = Guid.NewGuid();
        var body = new { orderId, clientRequestId = key, method = 0, amount = 60 };
        var result = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", body);
        Assert.True(result.GetProperty("finalized").GetBoolean());
        var next = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var nextId = next.GetProperty("orderId").GetInt32();
        // Simulate losing the previous success response, then retry after the next cart exists.
        result = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", body);
        Assert.True(result.GetProperty("finalized").GetBoolean());
        Assert.Equal(orderId, result.GetProperty("orderId").GetInt32());
        using (var changedOrder = await client.Http.PostAsJsonAsync($"/admin/pos/{nextId}/payments", new { clientRequestId = key, method = 0, amount = 60 }))
            Assert.Equal(HttpStatusCode.Conflict, changedOrder.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Single(await db.OrderPayments.Where(x => x.OrderId == orderId).ToListAsync());
            Assert.Empty(await db.OrderPayments.Where(x => x.OrderId == nextId).ToListAsync());
            Assert.Equal(OrderStatus.Draft, (await db.Orders.SingleAsync(x => x.Id == nextId)).Status);
            Assert.Equal(97, (await db.InventoryBalances.SingleAsync(x => x.ProductVariantId == store.VariantId)).OnHandQty);
            Assert.Equal(60, (await db.POSShifts.SingleAsync()).CashSalesTotal);
        }
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{nextId}/items?variantId={store.VariantId}&qty=3");
        var removedKey = Guid.NewGuid();
        var removable = new { clientRequestId = removedKey, method = 0, amount = 20 };
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{nextId}/payments", removable);
        int paymentId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId)) paymentId = (await db.OrderPayments.SingleAsync(x => x.OrderId == nextId)).Id;
        await client.JsonAsync(HttpMethod.Delete, $"/admin/pos/payments/{paymentId}");
        using (var retry = await client.Http.PostAsJsonAsync($"/admin/pos/{nextId}/payments", removable)) Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(0, (await db.Orders.SingleAsync(x => x.Id == nextId)).PaidTotal);
            Assert.Single(await db.OrderPayments.IgnoreQueryFilters().Where(x => x.StoreId == store.StoreId && x.ClientRequestId == removedKey).ToListAsync());
            // The database constraint must still protect the key after deletion, even if a writer bypasses the service.
            db.OrderPayments.Add(new GaoApp.Domain.Entities.OrderPayment {
                StoreId = store.StoreId, OrderId = nextId, ClientRequestId = removedKey, Amount = 20, Method = PaymentMethod.Cash
            });
            var duplicate = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            var sql = Assert.IsType<Microsoft.Data.SqlClient.SqlException>(duplicate.InnerException);
            Assert.Contains(sql.Number, new[] { 2601, 2627 });
        }
    }

    [Fact]
    public async Task Missing_identity_stale_cart_and_other_store_are_rejected_without_collecting()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var orderId = await StartOrderAsync(client, store);
        using (var missing = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/payments", new { method = 0, amount = 20 }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        foreach (var amount in new[] { 0m, -1m, 20.5m, 10000000000000000m })
        {
            using var invalid = await client.Http.PostAsJsonAsync($"/admin/pos/{orderId}/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using (var missingOrder = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", new { clientRequestId = Guid.NewGuid(), method = 0, amount = 20 }))
            Assert.Equal(HttpStatusCode.BadRequest, missingOrder.StatusCode);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var body = new { orderId, clientRequestId = Guid.NewGuid(), method = 0, amount = 20 };
        using (var stale = await client.Http.PostAsJsonAsync("/admin/pos/cart/current/payments", body)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        using (var crossStore = await foreign.Http.PostAsJsonAsync($"/admin/pos/{orderId}/payments", body)) Assert.Equal(HttpStatusCode.BadRequest, crossStore.StatusCode);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        Assert.Empty(await db.OrderPayments.ToListAsync());
    }

    private static async Task<int> StartOrderAsync(FullApplicationFixture.Client client, FullApplicationFixture.StoreSeed store)
    {
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var order = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var id = order.GetProperty("orderId").GetInt32();
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=3");
        return id;
    }
}
