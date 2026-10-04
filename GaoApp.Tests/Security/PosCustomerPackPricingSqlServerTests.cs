using System.Text.Json;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class PosCustomerPackPricingSqlServerTests
{
    [Fact]
    public async Task Customer_reprice_preserves_pack_thresholds_tiers_mixed_units_and_keep_price_choice()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        int bottleId, packId, boxId, retailId, wholesaleId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.Products.SingleAsync();
            var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
            product.Name = "Nước suối kiểm tra giá";
            product.BasePrice = 5000;
            variant.Price = 5000;
            var bottle = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                UnitId = product.BaseUnitId, Factor = 1, Price = 5000, WholesalePrice = 4500,
                IsBaseUnit = true, IsDefaultForSale = true,
                Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8934588063053", IsPrimary = true }] };
            var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                Unit = new Unit { StoreId = store.StoreId, Code = "PACK", Name = "Lốc" },
                Factor = 6, Price = 27000, WholesalePrice = 24000 };
            var box = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id,
                Unit = new Unit { StoreId = store.StoreId, Code = "BOX", Name = "Thùng" },
                Factor = 24, Price = 100000, WholesalePrice = 96000 };
            var retail = new Customer { StoreId = store.StoreId, Name = "Khách lẻ", PriceTier = "RETAIL" };
            var wholesale = new Customer { StoreId = store.StoreId, Name = "Khách sỉ", PriceTier = "WHOLESALE" };
            db.AddRange(bottle, pack, box, retail, wholesale);
            await db.SaveChangesAsync();
            (bottleId, packId, boxId, retailId, wholesaleId) = (bottle.Id, pack.Id, box.Id, retail.Id, wholesale.Id);
        }
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await client.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });

        async Task<JsonElement> Customer(int id, bool reprice = true) =>
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/cart/current/customer/{id}", new { repriceExistingLines = reprice });
        async Task<int> Cart()
        {
            var result = await client.JsonAsync(HttpMethod.Post, "/admin/pos/draft");
            return result.GetProperty("orderId").GetInt32();
        }
        async Task<JsonElement> Add(int orderId, int conversion, int quantity) =>
            await client.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&productUnitConversionId={conversion}&qty={quantity}");
        static void Total(JsonElement draft, decimal expected) => Assert.Equal(expected, draft.GetProperty("grandTotal").GetDecimal());

        // Reproduce the reported scan -> 24 bottles -> choose customer -> apply price flow.
        await Cart();
        var scanned = await client.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/scan", new { barcode = "8934588063053", qty = 1 });
        var scannedLine = scanned.GetProperty("lines")[0].GetProperty("lineId").GetInt32();
        Total(await client.JsonAsync(HttpMethod.Patch, $"/admin/pos/lines/{scannedLine}?qty=24"), 100000);
        Total(await Customer(retailId), 100000);
        Total(await Customer(wholesaleId), 96000);
        Total(await Customer(retailId), 100000);
        Total(await Customer(wholesaleId, false), 100000);
        Total(await client.JsonAsync(HttpMethod.Patch, $"/admin/pos/lines/{scannedLine}?qty=48"), 192000);

        foreach (var example in new (int Qty, decimal Retail, decimal Wholesale)[] { (3, 15000m, 13500m),
                     (6, 27000m, 24000m), (24, 100000m, 96000m), (48, 200000m, 192000m), (25, 104167m, 100000m) })
        {
            var orderId = await Cart();
            Total(await Add(orderId, bottleId, example.Qty), example.Retail);
            Total(await Customer(retailId), example.Retail);
            Total(await Customer(wholesaleId), example.Wholesale);
            Total(await Customer(retailId), example.Retail);
            await using var verify = app.Database.CreateTenantContext(store.StoreId);
            Assert.Equal(example.Retail, (await verify.Orders.SingleAsync(x => x.Id == orderId)).GrandTotal);
        }
        var mixedOrder = await Cart();
        await Add(mixedOrder, bottleId, 18);
        Total(await Add(mixedOrder, packId, 1), 100000);
        var mixed = await Customer(wholesaleId);
        Total(mixed, 96000);
        Assert.Equal(2, mixed.GetProperty("lines").GetArrayLength());
        Assert.Equal(24m, mixed.GetProperty("lines").EnumerateArray().Sum(x => x.GetProperty("baseQuantity").GetDecimal()));
        Total(await Customer(retailId), 100000);

        var boxOrder = await Cart();
        Total(await Add(boxOrder, boxId, 1), 100000);
        Total(await Customer(wholesaleId), 96000);
        // No wholesale pack price: fall back to that pack's retail price, not the bottle price.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == boxId)).WholesalePrice = null;
            await db.SaveChangesAsync();
        }
        Total(await Customer(wholesaleId), 100000);

        // Gift bottles must neither count toward the pack threshold nor acquire a sale price.
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var productId = await db.ProductVariants.Where(x => x.Id == store.VariantId).Select(x => x.ProductId).SingleAsync();
            db.Add(new Promotion { StoreId = store.StoreId, Name = "Mua 23 tặng 1", Type = PromotionType.BuyXGetY,
                IsActive = true, StartAtUtc = DateTime.UtcNow.AddDays(-1), EndAtUtc = DateTime.UtcNow.AddDays(1),
                BuyQuantity = 23, GetQuantity = 1, RequireGiftQuantityInCart = false,
                Items = [new PromotionItem { StoreId = store.StoreId, ProductId = productId, VariantId = store.VariantId }] });
            await db.SaveChangesAsync();
        }
        var giftOrder = await Cart();
        Total(await Add(giftOrder, bottleId, 23), 103500);
        var giftDraft = await Customer(retailId);
        Total(giftDraft, 103500);
        var gift = Assert.Single(giftDraft.GetProperty("lines").EnumerateArray(), x => x.GetProperty("isPromotionGift").GetBoolean());
        Assert.Equal(0m, gift.GetProperty("unitPrice").GetDecimal());
        Total(await Customer(wholesaleId), 92000);
    }
}
