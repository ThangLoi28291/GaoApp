using System.Text.Json;
using GaoApp.Application.DTOs.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data.Migrations;
using GaoApp.Tests.Configuration;
using GaoApp.Tests.Security;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Promotions;

[Collection("SqlServerConcurrency")]
public sealed class MixedQuantityPromotionSqlServerTests
{
    [Fact]
    public async Task Admin_and_pos_http_roundtrip_pool_flavours_and_selling_units_and_recalculate_after_edits()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        int productId, secondVariantId, packId, firstBaseId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var first = await db.ProductVariants.Include(x => x.Product).Include(x => x.UnitConversions)
                .SingleAsync(x => x.Id == store.VariantId);
            first.Price = 10000;
            first.Product.BasePrice = 10000;
            foreach (var conversion in first.UnitConversions) conversion.Price = 10000 * conversion.Factor;
            productId = first.ProductId;
            var second = new ProductVariant
            {
                StoreId = store.StoreId, ProductId = productId, Sku = "TH-MIX-SECOND",
                ProductVariantName = "TH ít đường", Price = 10000, CostPrice = 5000, IsActive = true
            };
            var packUnit = new Unit { StoreId = store.StoreId, Code = "MIX6", Name = "Lốc ghép 6", IsActive = true };
            var cartonUnit = new Unit { StoreId = store.StoreId, Code = "MIX48", Name = "Thùng ghép 48", IsActive = true };
            db.AddRange(second, packUnit, cartonUnit);
            await db.SaveChangesAsync();
            secondVariantId = second.Id;
            var firstBase = first.UnitConversions.FirstOrDefault(x => x.Factor == 1 && x.UnitId == first.Product.BaseUnitId);
            if (firstBase == null)
            {
                firstBase = new ProductUnitConversion
                {
                    StoreId = store.StoreId, ProductVariantId = first.Id, UnitId = first.Product.BaseUnitId,
                    Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true
                };
                db.Add(firstBase);
            }
            db.ProductUnitConversions.Add(new ProductUnitConversion
            {
                StoreId = store.StoreId, ProductVariantId = second.Id, UnitId = first.Product.BaseUnitId,
                Factor = 1, Price = 10000, IsBaseUnit = true, IsDefaultForSale = true
            });
            var pack = new ProductUnitConversion
            {
                StoreId = store.StoreId, ProductVariantId = second.Id,
                UnitId = packUnit.Id, Factor = 6, Price = 60000
            };
            db.Add(pack);
            foreach (var variant in new[] { first.Id, second.Id })
                db.ProductUnitConversions.Add(new ProductUnitConversion
                {
                    StoreId = store.StoreId, ProductVariantId = variant,
                    UnitId = cartonUnit.Id, Factor = 48, Price = 400000
                });
            db.InventoryBalances.Add(new InventoryBalance
            {
                StoreId = store.StoreId, WarehouseId = store.WarehouseId,
                ProductVariantId = second.Id, OnHandQty = 100
            });
            await db.SaveChangesAsync();
            packId = pack.Id;
            firstBaseId = firstBase.Id;
        }
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        var choices = await manager.JsonAsync(HttpMethod.Get, "/admin/promotion/searchproduct?term=TH-MIX-SECOND");
        Assert.Equal("TH ít đường", choices.EnumerateArray().Single().GetProperty("variantName").GetString());
        var request = new SavePromotionRequest
        {
            Name = "TH ghép vị SQL", Type = PromotionType.ComboFixedPrice,
            ComboPricingMode = ComboPricingMode.MixedQuantity, ComboQuantity = 48, ComboFixedPrice = 400000,
            StartAtUtc = DateTime.UtcNow.AddDays(-1), EndAtUtc = DateTime.UtcNow.AddDays(1),
            ComboRules = [new() { ProductId = productId, VariantId = store.VariantId },
                new() { ProductId = productId, VariantId = secondVariantId }]
        };
        var saved = await manager.JsonAsync(HttpMethod.Post, "/admin/promotion/save", request);
        Assert.True(saved.GetProperty("success").GetBoolean(), saved.ToString());
        var promotionId = saved.GetProperty("id").GetInt32();
        var edit = await manager.JsonAsync(HttpMethod.Get, $"/admin/promotion/get?id={promotionId}");
        Assert.Equal(2, edit.GetProperty("data").GetProperty("comboPricingMode").GetInt32());
        Assert.Equal(48, edit.GetProperty("data").GetProperty("comboQuantity").GetDecimal());
        Assert.Contains("promoComboPricingMode", await manager.Http.GetStringAsync("/admin/promotion"));
        await manager.JsonAsync(HttpMethod.Post, "/admin/pos/shift/open", new { openingCash = 0, warehouseId = store.WarehouseId });
        var cart = await manager.JsonAsync(HttpMethod.Post, "/admin/pos/cart/current/new", new { });
        var orderId = cart.GetProperty("orderId").GetInt32();
        await manager.JsonAsync(HttpMethod.Post, $"/admin/pos/{orderId}/items?variantId={store.VariantId}&productUnitConversionId={firstBaseId}&qty=24", new { });
        var draft = await manager.JsonAsync(HttpMethod.Post,
            $"/admin/pos/{orderId}/items?variantId={secondVariantId}&productUnitConversionId={packId}&qty=4", new { });
        Assert.Equal(400000, draft.GetProperty("grandTotal").GetDecimal());
        var lineId = draft.GetProperty("lines").EnumerateArray().Single(x => x.GetProperty("variantId").GetInt32() == store.VariantId)
            .GetProperty("lineId").GetInt32();
        draft = await manager.JsonAsync(HttpMethod.Patch, $"/admin/pos/lines/{lineId}?qty=26", new { });
        Assert.Equal(416667, draft.GetProperty("grandTotal").GetDecimal());
        await using (var verify = app.Database.CreateTenantContext(store.StoreId))
        {
            var order = await verify.Orders.Include(x => x.Lines).SingleAsync(x => x.Id == orderId);
            Assert.Equal(416667, order.GrandTotal);
            Assert.Equal(order.GrandTotal, order.Lines.Where(x => !x.IsDeleted).Sum(x => x.LineTotal));
            Assert.Equal(50, order.Lines.Where(x => !x.IsDeleted).Sum(x => x.BaseQuantity));
        }
        draft = await manager.JsonAsync(HttpMethod.Patch, $"/admin/pos/lines/{lineId}?qty=23", new { });
        Assert.Equal(470000, draft.GetProperty("grandTotal").GetDecimal());
        var offline = await manager.JsonAsync(HttpMethod.Get, "/admin/pos/offline/promotions");
        var cached = offline.EnumerateArray().Single(x => x.GetProperty("id").GetInt32() == promotionId);
        Assert.Equal(2, cached.GetProperty("comboPricingMode").GetInt32());
        Assert.Equal(48, cached.GetProperty("comboQuantity").GetDecimal());
    }

    [Fact]
    public async Task Schema_upgrade_defaults_legacy_combos_and_downgrade_preserves_existing_promotion_rows()
    {
        await using var sql = new InventoryPostingLocalDb();
        await sql.MigrateAsync("20261006173000_AddDeliveryPicking");
        var seed = await sql.SeedInventoryCatalogAsync();
        await using (var db = sql.CreateHostContext())
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO Promotions (StoreId,Name,Type,DiscountType,DiscountValue,ComboFixedPrice,
                    StartAtUtc,EndAtUtc,IsActive,Priority,RequireGiftQuantityInCart,CreatedAtUtc,IsDeleted)
                VALUES ({seed.StoreId},N'Legacy combo',2,1,0,400000,
                    SYSUTCDATETIME(),DATEADD(day,1,SYSUTCDATETIME()),1,0,1,SYSUTCDATETIME(),0)
                """);
        await sql.MigrateAsync();
        await using (var db = sql.CreateHostContext())
        {
            var promotion = await db.Promotions.SingleAsync();
            Assert.Equal(ComboPricingMode.RequiredItems, promotion.ComboPricingMode);
            Assert.Null(promotion.ComboQuantity);
            Assert.Null(promotion.ComboBaseUnitId);
            Assert.Equal(400000, promotion.ComboFixedPrice);
            var expected = new EfCoreDatabaseSchemaManifestCatalog(db).GetCurrentManifest();
            await db.Database.OpenConnectionAsync();
            var actual = await new SqlServerSchemaSnapshotReader(db).ReadAsync((await db.Database.GetAppliedMigrationsAsync()).ToArray());
            Assert.True(DatabaseSchemaComparer.Compare(expected, actual).IsMatch);
        }
        await sql.MigrateAsync("20261006173000_AddDeliveryPicking");
        await sql.MigrateAsync();
        await using var final = sql.CreateHostContext();
        Assert.Equal("Legacy combo", (await final.Promotions.SingleAsync()).Name);
    }
}
