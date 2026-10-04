using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ProductPriceQuickViewSqlServerTests
{
    internal sealed record PriceSeed(int ProductId, int VariantId, int PackId, int CartonId);

    internal static async Task<PriceSeed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        var seed = await ReceiptBarcodeProposalSqlServerTests.SeedAsync(app, store);
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var variant = await db.ProductVariants.Include(x => x.Product).ThenInclude(x => x.Supplier).SingleAsync(x => x.Id == store.VariantId);
        variant.Product.Supplier.Name = "NCC-PRIVATE-ONLY";
        variant.Product.BasePrice = 12000; variant.Price = 12500; variant.WholesalePrice = 8888;
        var baseUnit = await db.ProductUnitConversions.SingleOrDefaultAsync(x => x.ProductVariantId == variant.Id && x.UnitId == variant.Product.BaseUnitId && x.Factor == 1);
        if (baseUnit == null)
        {
            baseUnit = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = variant.Product.BaseUnitId, IsBaseUnit = true };
            db.Add(baseUnit);
        }
        baseUnit.Price = 12000; baseUnit.WholesalePrice = 9500;
        (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).WholesalePrice = 45000;
        (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.CartonId)).WholesalePrice = null;
        await db.SaveChangesAsync();
        return new(variant.ProductId, variant.Id, seed.PackId, seed.CartonId);
    }

    [Fact]
    public async Task List_and_popup_keep_supplier_private_and_show_tenant_scoped_prices_per_variant_and_unit_without_writes()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View));
        using var manager = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View, PermissionCodes.Catalog.Supplier.View));
        using var foreign = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], PermissionCodes.Catalog.Product.View));
        using var denied = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        const string index = "/Admin/Product?lifecycle=all";
        const string search = "/Admin/Product/Search?lifecycle=all";
        static JsonElement Units(string html)
        {
            var match = Regex.Match(html, "data-quick-units=\"([^\"]*)\""); Assert.True(match.Success);
            return JsonSerializer.Deserialize<JsonElement>(WebUtility.HtmlDecode(match.Groups[1].Value));
        }
        await using var before = app.Database.CreateTenantContext(store.StoreId);
        var productVersion = (await before.Products.AsNoTracking().SingleAsync(x => x.Id == seed.ProductId)).RowVersion;
        foreach (var url in new[] { index, search })
        {
            var html = await staff.Http.GetStringAsync(url);
            Assert.DoesNotContain("NCC-PRIVATE-ONLY", html);
            Assert.DoesNotContain("id=\"productQuickViewSupplier\"", html);
            Assert.Contains("Giá sỉ: 9.500", WebUtility.HtmlDecode(html));
            var units = Units(html).EnumerateArray().ToList(); Assert.Equal(3, units.Count);
            var pack = Assert.Single(units, x => x.GetProperty("unitName").GetString() == "Lốc");
            Assert.Equal(48000, pack.GetProperty("retailPrice").GetDecimal()); Assert.Equal(45000, pack.GetProperty("wholesalePrice").GetDecimal());
            var carton = Assert.Single(units, x => x.GetProperty("unitName").GetString() == "Thùng");
            Assert.Equal(280000, carton.GetProperty("retailPrice").GetDecimal()); Assert.Equal(JsonValueKind.Null, carton.GetProperty("wholesalePrice").ValueKind);
            Assert.All(units, x => { Assert.False(x.TryGetProperty("supplierName", out _)); Assert.False(x.TryGetProperty("costPrice", out _)); });
            Assert.Contains("NCC-PRIVATE-ONLY", await manager.Http.GetStringAsync(url));
        }
        Assert.Contains("id=\"productQuickViewSupplier\"", await manager.Http.GetStringAsync(index));
        Assert.DoesNotContain("NCC-PRIVATE-ONLY", await foreign.Http.GetStringAsync(index));
        using (var response = await denied.Http.GetAsync(index)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            Assert.Equal(productVersion, (await db.Products.SingleAsync(x => x.Id == seed.ProductId)).RowVersion);
            var baseUnit = await db.ProductUnitConversions.SingleAsync(x => x.ProductVariantId == seed.VariantId && x.Factor == 1);
            baseUnit.IsDeleted = true;
            (await db.ProductUnitConversions.SingleAsync(x => x.Id == seed.PackId)).IsActive = false;
            db.ProductVariants.Add(new ProductVariant { StoreId = store.StoreId, ProductId = seed.ProductId, Sku = "PRICE-VARIANT-2",
                ProductVariantName = "Biến thể thứ hai", Price = 15000, WholesalePrice = 14000, IsActive = true });
            await db.SaveChangesAsync();
        }
        var changedHtml = await staff.Http.GetStringAsync(search);
        var changed = Units(changedHtml).EnumerateArray().ToList(); Assert.Equal(4, changed.Count);
        var fallback = Assert.Single(changed, x => x.GetProperty("variantId").GetInt32() == seed.VariantId && x.GetProperty("isBaseUnit").GetBoolean());
        Assert.Equal(12500, fallback.GetProperty("retailPrice").GetDecimal()); Assert.Equal(8888, fallback.GetProperty("wholesalePrice").GetDecimal());
        Assert.False(Assert.Single(changed, x => x.GetProperty("unitName").GetString() == "Lốc").GetProperty("isActive").GetBoolean());
        Assert.Contains("8.888 – 14.000", WebUtility.HtmlDecode(changedHtml));
        await using var check = app.Database.CreateTenantContext(store.StoreId);
        Assert.Equal(100, (await check.InventoryBalances.SingleAsync()).OnHandQty);
    }
}
