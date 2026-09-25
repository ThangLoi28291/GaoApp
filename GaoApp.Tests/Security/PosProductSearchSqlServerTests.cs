using System.Text;
using GaoApp.Application.Common.Helpers;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class PosProductSearchSqlServerTests
{
    [Fact]
    public async Task Pos_search_returns_the_same_products_with_or_without_accents_despite_missing_legacy_search_names()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var store = app.Stores[0];
        var expected = new List<int>();
        int unitVariantId;
        await using (var db = app.Database.CreateTenantContext(store.StoreId))
        {
            var product = await db.Products.SingleAsync();
            product.Name = "Nhóm hàng kiểm thử";
            var first = await db.ProductVariants.SingleAsync();
            first.ProductVariantName = "Sữa tươi TH có đường 180ml";
            first.ProductVariantNameNormalized = ProductVariantNameHelper.NormalizeForSearch(first.ProductVariantName);
            expected.Add(first.Id);
            unitVariantId = first.Id;
            var variants = new[] {
                new ProductVariant { StoreId = store.StoreId, ProductId = product.Id, Sku = "TH-NONE",
                    ProductVariantName = "Sữa tươi TH không đường 180ml", ProductVariantNameNormalized = null },
                new ProductVariant { StoreId = store.StoreId, ProductId = product.Id, Sku = "TH-EMPTY",
                    ProductVariantName = "Sữa tươi TH ít đường 1L", ProductVariantNameNormalized = "" },
                new ProductVariant { StoreId = store.StoreId, ProductId = product.Id, Sku = "TH-STALE",
                    ProductVariantName = "SỮA TƯƠI TH hương dâu 110ml", ProductVariantNameNormalized = "ten cu khong con dung" }
            };
            db.AddRange(variants);
            var parentOnly = NewProduct(product, "PARENT-NAME", "Sữa tươi TH nguyên chất 1L");
            var parentVariant = new ProductVariant { StoreId = store.StoreId, Product = parentOnly, Sku = "TH-PARENT" };
            db.Add(parentVariant);
            var pending = NewProduct(product, "PENDING", "Sữa tươi TH hàng chờ bán");
            pending.IsSellable = false;
            var inactive = NewProduct(product, "INACTIVE-PRODUCT", "Sữa tươi TH ngừng kinh doanh");
            inactive.IsActive = false;
            var deleted = NewProduct(product, "DELETED-PRODUCT", "Sữa tươi TH đã xóa");
            deleted.IsDeleted = true;
            db.AddRange(
                new ProductVariant { StoreId = store.StoreId, Product = pending, Sku = "PENDING" },
                new ProductVariant { StoreId = store.StoreId, Product = inactive, Sku = "INACTIVE-PRODUCT" },
                new ProductVariant { StoreId = store.StoreId, Product = deleted, Sku = "DELETED-PRODUCT" },
                new ProductVariant { StoreId = store.StoreId, ProductId = product.Id, Sku = "INACTIVE-VARIANT", ProductVariantName = "Sữa tươi TH ngừng bán", IsActive = false },
                new ProductVariant { StoreId = store.StoreId, ProductId = product.Id, Sku = "DELETED-VARIANT", ProductVariantName = "Sữa tươi TH xóa", IsDeleted = true });
            var unit = new Unit { StoreId = store.StoreId, Code = "MILK-PACK", Name = "Thùng đóng sẵn" };
            db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = first.Id, Unit = unit, Factor = 24, Price = 120000 });
            db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariant = variants[0], Unit = unit, IsActive = false });
            var deletedConversion = new ProductUnitConversion { StoreId = store.StoreId, ProductVariant = variants[1], Unit = unit };
            var deletedUnit = new Unit { StoreId = store.StoreId, Code = "OLD-PACK", Name = "Thùng đóng sẵn đã xóa" };
            db.Add(deletedConversion);
            db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariant = variants[2], Unit = deletedUnit });
            // A variant matching both its name and a unit must appear only once.
            db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = first.Id,
                Unit = new Unit { StoreId = store.StoreId, Code = "NAME-MATCH", Name = "Sữa tươi TH" } });
            await db.SaveChangesAsync();
            // New rows start undeleted in AppDbContext; apply the legacy deleted state after insertion.
            deleted.IsDeleted = true;
            deletedConversion.IsDeleted = true;
            deletedUnit.IsDeleted = true;
            (await db.ProductVariants.SingleAsync(x => x.Sku == "DELETED-VARIANT")).IsDeleted = true;
            await db.SaveChangesAsync();
            expected.AddRange(variants.Select(x => x.Id));
            expected.Add(parentVariant.Id);
        }
        await using (var foreign = app.Database.CreateTenantContext(app.Stores[1].StoreId))
        {
            var variant = await foreign.ProductVariants.SingleAsync();
            variant.ProductVariantName = "Sữa tươi TH cửa hàng khác";
            variant.ProductVariantNameNormalized = "sua tuoi th cua hang khac";
            await foreign.SaveChangesAsync();
        }
        using var client = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Pos.Order.View));
        var accented = await Search("sữa tươi th");
        Assert.Equal(expected.Order(), accented.Order());
        foreach (var keyword in new[] { "sua tuoi th", "SUA TUOI TH", "Sữa Tươi TH", "  sua tuoi th  ", "sữa tươi th".Normalize(NormalizationForm.FormD) })
            Assert.Equal(accented, await Search(keyword));

        // The same query rules cover parent-only names and selling-unit names, including đ -> d.
        Assert.Equal(await Search("không đường"), await Search("khong duong"));
        Assert.Single(await Search("khong duong"));
        Assert.Equal(new[] { unitVariantId }, await Search("thung dong san"));
        Assert.Equal(await Search("Thùng đóng sẵn"), await Search("thung dong san"));
        Assert.Equal(accented.Take(2), await Search("sua tuoi th", 2));
        Assert.Single(await Search("TH-NONE"));

        async Task<int[]> Search(string keyword, int take = 10)
        {
            var response = await client.JsonAsync(HttpMethod.Get, $"/admin/pos/products/search?keyword={Uri.EscapeDataString(keyword)}&take={take}");
            return response.EnumerateArray().Select(x => x.GetProperty("variantId").GetInt32()).ToArray();
        }
    }

    private static Product NewProduct(Product template, string alias, string name)
        => new() { StoreId = template.StoreId, Name = name, Alias = alias,
            CategoryId = template.CategoryId, SupplierId = template.SupplierId, BaseUnitId = template.BaseUnitId };
}
