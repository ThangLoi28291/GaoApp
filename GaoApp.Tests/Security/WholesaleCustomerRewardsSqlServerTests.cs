using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class WholesaleCustomerRewardsSqlServerTests
{
    [Fact]
    public async Task Wholesale_customers_cannot_earn_or_redeem_and_points_stay_hidden_without_losing_historical_balances()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        int wholesaleId, retailId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var wholesale = new Customer { StoreId = store.StoreId, Name = "Khách sỉ", PriceTier = "WHOLESALE" };
            var retail = new Customer { StoreId = store.StoreId, Name = "Khách lẻ", PriceTier = "RETAIL" };
            var product = await db.Products.SingleAsync();
            var variant = await db.ProductVariants.SingleAsync();
            product.BasePrice = 6000;
            variant.Price = 6000;
            product.IsRewardEligibleOverride = true;
            db.AddRange(wholesale, retail,
                new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId,
                    Factor = 1, IsBaseUnit = true, IsDefaultForSale = true, Price = 6000 },
                new RewardSettings { StoreId = store.StoreId, IsEnabled = true, MoneyPerPoint = 1000, PointsPerVoucher = 5, VoucherValue = 10000 },
                new CustomerRewardLedger { StoreId = store.StoreId, Customer = wholesale, Type = CustomerRewardLedgerType.ImportOldBalance, Amount = 50000 },
                new CustomerRewardVoucher { StoreId = store.StoreId, Customer = wholesale, VoucherCode = "WH-READY", Value = 10000,
                    RequiredAmount = 5000, Status = CustomerRewardVoucherStatus.Available });
            await db.SaveChangesAsync();
            wholesaleId = wholesale.Id;
            retailId = retail.Id;
        }
        var shift = await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var first = await SellAsync(wholesaleId, 6000); // No wholesale price: still must not earn at the retail fallback price.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.ProductUnitConversions.SingleAsync()).WholesalePrice = 5000;
            await db.SaveChangesAsync();
        }
        await SellAsync(wholesaleId, 5000);
        var summary = await client.JsonAsync(HttpMethod.Get, $"/admin/api/customers/{wholesaleId}/reward-summary");
        Assert.False(summary.GetProperty("isPointEligible").GetBoolean());
        Assert.Equal(0, summary.GetProperty("availablePoints").GetDecimal());
        Assert.Equal(1, summary.GetProperty("availableVoucherCount").GetInt32());
        var balance = await client.JsonAsync(HttpMethod.Get, $"/admin/api/customers/{wholesaleId}/reward-balance");
        Assert.Equal(0, balance.GetProperty("balanceAmount").GetDecimal());
        using (var denied = await client.Http.PostAsJsonAsync("/admin/api/customers/redeem-voucher", new { customerId = wholesaleId, voucherCount = 1 }))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var denied = await client.Http.PostAsJsonAsync("/admin/api/customers/manual-ledger", new { customerId = wholesaleId, amount = 1000 }))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using (var denied = await foreign.Http.GetAsync($"/admin/api/customers/{wholesaleId}/reward-summary"))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

        var profile = $"/admin/customers/{wholesaleId}/profile";
        var html = await client.Http.GetStringAsync(profile);
        Assert.DoesNotContain("id=\"cpPoints\"", html);
        Assert.DoesNotContain("data-tab=\"points\"", html);
        Assert.Equal(0, (await client.JsonAsync(HttpMethod.Get, profile + "/data?tab=points")).GetProperty("total").GetInt32());
        Assert.DoesNotContain((await client.JsonAsync(HttpMethod.Get, profile + "/data")).GetProperty("items").EnumerateArray(),
            x => x.GetProperty("kind").GetString() == "points");
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(50000, await db.CustomerRewardLedgers.Where(x => x.CustomerId == wholesaleId).SumAsync(x => x.Amount));
            Assert.Single(await db.CustomerRewardLedgers.Where(x => x.CustomerId == wholesaleId).ToListAsync());
            Assert.Single(await db.CustomerRewardVouchers.Where(x => x.CustomerId == wholesaleId).ToListAsync());
            (await db.Customers.SingleAsync(x => x.Id == wholesaleId)).PriceTier = "RETAIL";
            await db.SaveChangesAsync();
        }
        Assert.Equal(0, (await client.JsonAsync(HttpMethod.Get, $"/admin/api/orders/{first}/reward-calculation")).GetProperty("rewardableAmount").GetDecimal());

        var retailSale = await SellAsync(retailId, 6000);
        var redeemed = await client.JsonAsync(HttpMethod.Post, "/admin/api/customers/redeem-voucher", new { customerId = retailId, voucherCount = 1 });
        Assert.Equal(1, redeemed.GetProperty("balance").GetProperty("availablePoints").GetDecimal());
        int lineId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.Customers.SingleAsync(x => x.Id == retailId)).PriceTier = "WHOLESALE";
            lineId = (await db.OrderLines.SingleAsync(x => x.OrderId == retailSale)).Id;
            await db.SaveChangesAsync();
        }
        var oldRetail = await client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{retailSale}/receipt");
        Assert.Equal(JsonValueKind.Null, oldRetail.GetProperty("customerCurrentPoints").ValueKind);
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/returns", new {
            orderId = retailSale, posShiftId = shift.GetProperty("id").GetInt32(), type = (int)SalesReturnType.ReturnAndRefund, reason = "Trả đơn bán lẻ trước khi đổi nhóm khách",
            lines = new[] { new { orderLineId = lineId, returnQuantity = 1, returnBaseQuantity = 1, refundUnitAmount = 6000, action = (int)SalesReturnLineAction.Restock } },
            payments = new[] { new { method = (int)PaymentMethod.Cash, amount = 6000 } } });
        await using var verify = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(-6000, await verify.CustomerRewardLedgers.Where(x => x.OrderId == retailSale && x.Type == CustomerRewardLedgerType.ReturnDeducted).SumAsync(x => x.Amount));

        async Task<int> SellAsync(int customerId, decimal amount)
        {
            var draft = await client.JsonAsync(HttpMethod.Post, $"/admin/pos/draft?customerId={customerId}");
            var id = draft.GetProperty("orderId").GetInt32();
            if (customerId == wholesaleId)
            {
                var screen = await client.JsonAsync(HttpMethod.Get, "/admin/pos/screen");
                Assert.False(screen.GetProperty("currentDraft").GetProperty("rewardSummary").GetProperty("isPointEligible").GetBoolean());
            }
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{id}/items?variantId={store.VariantId}&qty=1");
            await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/payment-and-finalize", new { orderId = id, clientRequestId = Guid.NewGuid(), method = 0, amount });
            var receipt = await client.JsonAsync(HttpMethod.Get, $"/admin/pos/orders/{id}/receipt");
            if (customerId == wholesaleId)
            {
                Assert.Equal("WHOLESALE", receipt.GetProperty("customerPriceTier").GetString());
                Assert.Equal(JsonValueKind.Null, receipt.GetProperty("customerCurrentPoints").ValueKind);
                await using var db = app.Database.CreateTenantContext(store.StoreId);
                Assert.Empty(await db.CustomerRewardLedgers.Where(x => x.OrderId == id).ToListAsync());
                Assert.Equal(0, (await db.OrderLines.SingleAsync(x => x.OrderId == id)).RewardableAmountSnapshot);
            }
            else Assert.Equal(6, receipt.GetProperty("customerCurrentPoints").GetDecimal());
            return id;
        }
    }
}
