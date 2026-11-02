using System.Net;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ReceiptCustomerPointsSqlServerTests
{
    [Fact]
    public async Task Receipt_reads_current_points_after_checkout_and_adjustment_without_blocking_unconfigured_stores()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        int customerId, zeroCustomerId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var customer = new Customer { StoreId = store.StoreId, Name = "Khách in điểm" };
            var zeroCustomer = new Customer { StoreId = store.StoreId, Name = "Khách chưa tích điểm" };
            db.AddRange(customer, zeroCustomer);
            await db.SaveChangesAsync();
            customerId = customer.Id;
            zeroCustomerId = zeroCustomer.Id;
        }
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/draft?customerId={customerId}");
        var orderId = draft.GetProperty("orderId").GetInt32();
        var endpoint = $"/admin/pos/orders/{orderId}/receipt";
        var unconfigured = await client.JsonAsync(HttpMethod.Get, endpoint);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, unconfigured.GetProperty("customerCurrentPoints").ValueKind);

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.Products.SingleAsync();
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            product.BasePrice = 6000;
            variant.Price = 6000;
            product.IsRewardEligibleOverride = true;
            db.AddRange(
                new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId,
                    Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 6000 },
                new RewardSettings { StoreId = store.StoreId, IsEnabled = true, MoneyPerPoint = 1000, PointsPerVoucher = 100, VoucherValue = 10000 },
                new CustomerRewardLedger { StoreId = store.StoreId, CustomerId = customerId, Type = CustomerRewardLedgerType.ManualAdjust, Amount = 12499 });
            await db.SaveChangesAsync();
        }
        Assert.Equal(12, (await client.JsonAsync(HttpMethod.Get, endpoint)).GetProperty("customerCurrentPoints").GetDecimal());
        await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=1");
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize",
            new { orderId, clientRequestId = Guid.NewGuid(), method = 0, amount = 6000 });
        Assert.Equal(18, (await client.JsonAsync(HttpMethod.Get, endpoint)).GetProperty("customerCurrentPoints").GetDecimal());

        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            db.Add(new CustomerRewardLedger { StoreId = store.StoreId, CustomerId = customerId,
                Type = CustomerRewardLedgerType.ManualAdjust, Amount = -10500 });
            (await db.RewardSettings.SingleAsync()).IsEnabled = false;
            await db.SaveChangesAsync();
        }
        // Reprinting reflects the latest balance, even when new point earning is disabled.
        Assert.Equal(7, (await client.JsonAsync(HttpMethod.Get, endpoint)).GetProperty("customerCurrentPoints").GetDecimal());
        Assert.Contains("\"customerCurrentPoints\":7", await client.Http.GetStringAsync($"/admin/pos/orders/{orderId}/print?autoPrint=false"));
        using (var denied = await foreign.Http.GetAsync(endpoint))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        var zeroDraft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/draft?customerId={zeroCustomerId}");
        var zeroId = zeroDraft.GetProperty("orderId").GetInt32();
        Assert.Equal(0, (await client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{zeroId}/receipt")).GetProperty("customerCurrentPoints").GetDecimal());
        var guestDraft = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
        var guestId = guestDraft.GetProperty("orderId").GetInt32();
        Assert.Equal(System.Text.Json.JsonValueKind.Null,
            (await client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{guestId}/receipt")).GetProperty("customerCurrentPoints").ValueKind);
    }
}
