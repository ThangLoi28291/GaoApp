using System.Net;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProductBaseUnitSqlServerTests
{
    [Fact]
    public async Task Product_edit_synchronizes_all_base_units_preserves_barcodes_and_rejects_conflicts_and_stale_unit_edits()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var template = await db.Products.AsNoTracking().SingleAsync();
        var oldUnit = new Unit { StoreId = store.StoreId, Code = "BASE-OLD", Name = "Bịch", IsActive = true };
        var newUnit = new Unit { StoreId = store.StoreId, Code = "BASE-NEW", Name = "Bánh", IsActive = true };
        var packUnit = new Unit { StoreId = store.StoreId, Code = "BASE-PACK", Name = "Thùng", IsActive = true };
        db.AddRange(newUnit, packUnit);
        var product = new Product
        {
            StoreId = store.StoreId, Name = "Bánh thử đồng bộ", Alias = "base-unit-test", BaseUnit = oldUnit,
            SupplierId = template.SupplierId, CategoryId = template.CategoryId, IsActive = true,
            Variants = Enumerable.Range(1, 2).Select(i => new ProductVariant
            {
                StoreId = store.StoreId, Sku = "base-sync-" + i, ProductVariantName = "Bánh " + i, CostPrice = 4000, IsActive = i == 1,
                UnitConversions = [new ProductUnitConversion
                {
                    StoreId = store.StoreId, Unit = oldUnit, Factor = 1, IsBaseUnit = true, IsDefaultForSale = true,
                    Price = 10000, WholesalePrice = 9000, IsActive = true,
                    Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "BASE-SYNC-" + i, IsPrimary = true, IsActive = true }]
                }]
            }).ToList()
        };
        db.Add(product); await db.SaveChangesAsync();
        var variants = product.Variants.OrderBy(x => x.Id).ToArray();
        var bases = variants.Select(x => x.UnitConversions.Single()).ToArray();
        var barcodeIds = bases.Select(x => x.Barcodes.Single().Id).Order().ToArray();
        var pack = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variants[1].Id, UnitId = packUnit.Id,
            Factor = 24, Price = 200000, IsDefaultForSale = true, IsActive = true };
        bases[1].IsDefaultForSale = false;
        db.Add(pack); await db.SaveChangesAsync();

        async Task<(HttpStatusCode Status, string Html)> Edit(int unitId, string name = "Bánh thử đồng bộ")
        {
            using var response = await client.Http.PostAsync("/Admin/Product/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Id"] = product.Id.ToString(), ["Name"] = name, ["Alias"] = product.Alias,
                ["CategoryId"] = product.CategoryId.ToString(), ["SupplierId"] = product.SupplierId.ToString(),
                ["BaseUnitId"] = unitId.ToString(), ["BasePrice"] = "10000", ["ImagesStateJson"] = "[]"
            }));
            return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }

        // One variant conflicts: no other variant/product is allowed to change first.
        var conflict = await Edit(packUnit.Id, "Tên không được lưu khi lỗi");
        Assert.Equal(HttpStatusCode.OK, conflict.Status);
        Assert.Contains("đã có trong bảng quy đổi", conflict.Html);
        Assert.All(await db.ProductUnitConversions.AsNoTracking().Where(x => x.ProductVariant.ProductId == product.Id && x.IsBaseUnit).ToListAsync(), x => Assert.Equal(oldUnit.Id, x.UnitId));
        Assert.Equal(product.Name, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).Name);

        var saved = await Edit(newUnit.Id);
        Assert.Equal(HttpStatusCode.Redirect, saved.Status);
        Assert.Equal(newUnit.Id, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).BaseUnitId);
        var updated = await db.ProductUnitConversions.AsNoTracking().Where(x => x.ProductVariant.ProductId == product.Id).ToListAsync();
        Assert.Equal(3, updated.Count);
        Assert.All(updated.Where(x => x.IsBaseUnit), x => { Assert.Equal(newUnit.Id, x.UnitId); Assert.Equal(1, x.Factor); Assert.Equal(10000, x.Price); Assert.Equal(9000, x.WholesalePrice); });
        Assert.Equal(barcodeIds, await db.ProductVariantUnitBarcodes.AsNoTracking().Where(x => x.ProductUnitConversion.ProductVariant.ProductId == product.Id).OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        Assert.Equal(24, updated.Single(x => x.Id == pack.Id).Factor);
        Assert.True(updated.Single(x => x.Id == pack.Id).IsDefaultForSale);
        Assert.False(updated.Single(x => x.Id == bases[1].Id).IsDefaultForSale);
        var cards = await client.JsonAsync(HttpMethod.Get, $"/Admin/Product/VariantsData?productId={product.Id}");
        Assert.All(cards.GetProperty("data").EnumerateArray(), x => Assert.Equal("Bánh", x.GetProperty("baseUnitName").GetString()));

        async Task AssertUnitRejected(object request)
        {
            var response = await client.JsonAsync(HttpMethod.Post, "/Admin/ProductUnitConversion/SaveConversion", request);
            Assert.False(response.GetProperty("ok").GetBoolean(), response.ToString());
            Assert.Contains("Đơn vị", response.GetProperty("message").GetString());
        }
        await AssertUnitRejected(new { id = bases[0].Id, productVariantId = variants[0].Id, unitId = oldUnit.Id, factor = 1, isBaseUnit = true, isActive = true });
        await AssertUnitRejected(new { id = bases[0].Id, productVariantId = variants[0].Id, unitId = newUnit.Id, factor = 1, isBaseUnit = false, isActive = true });
        await AssertUnitRejected(new { id = bases[0].Id, productVariantId = variants[0].Id, unitId = newUnit.Id, factor = 2, isBaseUnit = true, isActive = true });
        await AssertUnitRejected(new { productVariantId = variants[0].Id, unitId = oldUnit.Id, factor = 1, isBaseUnit = true, isActive = true });
        var priceEdit = await client.JsonAsync(HttpMethod.Post, "/Admin/ProductUnitConversion/SaveConversion",
            new { id = bases[0].Id, productVariantId = variants[0].Id, unitId = newUnit.Id, factor = 1, isBaseUnit = true, isDefaultForSale = true, isActive = true, price = 12000, wholesalePrice = 11000 });
        Assert.True(priceEdit.GetProperty("ok").GetBoolean(), priceEdit.ToString());

        // A wrong-store unit cannot be attached through a forged form.
        await using var otherDb = app.Database.CreateTenantContext(app.Stores[1].StoreId);
        var otherUnitId = await otherDb.Units.Select(x => x.Id).FirstAsync();
        Assert.Contains("không thuộc cửa hàng", (await Edit(otherUnitId)).Html);
        Assert.Equal(newUnit.Id, (await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id)).BaseUnitId);

        // A legacy mismatch can be repaired without changing the conversion identity.
        db.ChangeTracker.Clear();
        var legacy = await db.Products.SingleAsync(x => x.Id == product.Id);
        legacy.BaseUnitId = oldUnit.Id; await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect, (await Edit(oldUnit.Id)).Status);
        Assert.All(await db.ProductUnitConversions.AsNoTracking().Where(x => x.ProductVariant.ProductId == product.Id && x.IsBaseUnit).ToListAsync(), x => Assert.Equal(oldUnit.Id, x.UnitId));
    }

    [Fact]
    public async Task Changing_used_base_unit_is_rejected_without_saving_product_or_conversions()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var product = await db.Products.SingleAsync();
        var conversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = store.VariantId, UnitId = product.BaseUnitId, Factor = 1, IsBaseUnit = true, IsActive = true };
        var target = new Unit { StoreId = store.StoreId, Code = "USED-TARGET", Name = "Đơn vị khác", IsActive = true };
        db.AddRange(conversion, target); await db.SaveChangesAsync();
        using var response = await client.Http.PostAsync("/Admin/Product/Edit", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Id"] = product.Id.ToString(), ["Name"] = "Không được lưu", ["Alias"] = product.Alias,
            ["CategoryId"] = product.CategoryId.ToString(), ["SupplierId"] = product.SupplierId.ToString(),
            ["BaseUnitId"] = target.Id.ToString(), ["BasePrice"] = "10000", ["ImagesStateJson"] = "[]"
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("đã có chứng từ hoặc số dư tồn kho", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        var saved = await db.Products.AsNoTracking().SingleAsync(x => x.Id == product.Id);
        Assert.Equal(product.Name, saved.Name); Assert.Equal(product.BaseUnitId, saved.BaseUnitId);
        Assert.Equal(product.BaseUnitId, (await db.ProductUnitConversions.AsNoTracking().SingleAsync(x => x.Id == conversion.Id)).UnitId);
    }
}
