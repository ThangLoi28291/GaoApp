using System.Net;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Repositories.Products;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("R1FinalDatabasePreflight")]
public sealed class ProductFiltersSqlServerTests
{
    internal sealed record FilterSeed(int SupplierId, int BrandId, int UnitId, int ProductId, int OtherSupplierId);
    internal static async Task<FilterSeed> SeedAsync(FullApplicationFixture app, FullApplicationFixture.StoreSeed store)
    {
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var product = await db.Products.Include(x => x.Supplier).Include(x => x.BaseUnit).SingleAsync();
        product.Name = "Sữa tươi nguyên chất"; product.Alias = "filter-milk";
        product.Supplier.Name = "Nguyễn Phát"; product.Supplier.Code = "NCC-NGUYEN"; product.Supplier.IsActive = false;
        product.BaseUnit.Name = "Chai";
        var brand = new Brand { StoreId = store.StoreId, Code = "AD", Name = "Ánh Dương", IsActive = true };
        product.Brand = brand;
        db.Add(new ProductImage { StoreId = store.StoreId, ProductId = product.Id, IsPrimary = true,
            MediaAsset = new MediaAsset { StoreId = store.StoreId, StoragePath = "uploads/test-product-filter.svg", ContentType = "image/svg+xml" } });
        var variant = await db.ProductVariants.SingleAsync(x => x.Id == store.VariantId);
        db.Add(new ProductUnitConversion { StoreId = store.StoreId, ProductVariantId = variant.Id, UnitId = product.BaseUnitId, IsBaseUnit = true, IsActive = true,
            Barcodes = [new ProductVariantUnitBarcode { StoreId = store.StoreId, Barcode = "8938505974194", IsActive = true, IsPrimary = true }] });
        var supplier = new Supplier { StoreId = store.StoreId, Code = "NCC-OTHER", Name = "Hà Nội", IsActive = true };
        var unit = new Unit { StoreId = store.StoreId, Code = "FILTER-PACK", Name = "Gói", IsActive = true };
        Product Make(string alias, string name, bool active) => new Product { StoreId = store.StoreId, CategoryId = product.CategoryId, BaseUnit = unit,
            Supplier = supplier, Alias = alias, Name = name, BasePrice = 20000, IsActive = active, IsSellable = true,
            Variants = [new ProductVariant { StoreId = store.StoreId, Sku = alias, ProductVariantName = name, IsActive = true }] };
        db.Add(Make("filter-other", "Bánh gạo rong biển", true));
        var inactive = Make("filter-inactive", "Sữa tươi ngừng bán", false);
        inactive.Supplier = product.Supplier; inactive.BaseUnit = product.BaseUnit; inactive.Brand = brand;
        db.Add(inactive);
        await db.SaveChangesAsync();
        return new(product.SupplierId, brand.Id, product.BaseUnitId, product.Id, supplier.Id);
    }

    [Fact]
    public async Task Product_filters_combine_before_paging_and_lookups_are_accent_insensitive_tenant_scoped_and_authorized()
    {
        await using var app = await FullApplicationFixture.StartAsync(); var store = app.Stores[0];
        var seed = await SeedAsync(app, store);
        using var admin = await app.LoginAsync(await app.AddAccountAsync(store, "*"));
        using var staff = await app.LoginAsync(await app.AddAccountAsync(store, PermissionCodes.Catalog.Product.View));
        using var outsider = await app.LoginAsync(await app.AddAccountAsync(app.Stores[1], "*"));
        var supplier = await admin.JsonAsync(HttpMethod.Get, "/Admin/Product/FilterOptions?kind=supplier&term=nguyen");
        Assert.Single(supplier.EnumerateArray());
        Assert.Equal(seed.SupplierId, supplier[0].GetProperty("id").GetInt32());
        Assert.False(supplier[0].GetProperty("isActive").GetBoolean());
        var brands = await admin.JsonAsync(HttpMethod.Get, "/Admin/Product/FilterOptions?kind=brand&term=ANH%20DUONG");
        Assert.Single(brands.EnumerateArray());
        Assert.Empty((await outsider.JsonAsync(HttpMethod.Get, "/Admin/Product/FilterOptions?kind=brand&selectedId=" + seed.BrandId)).EnumerateArray());
        using (var blocked = await staff.Http.GetAsync("/Admin/Product/FilterOptions?kind=supplier&term=nguyen")) Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using (var blocked = await staff.Http.GetAsync("/Admin/Product/Search?supplierId=" + seed.SupplierId)) Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        using (var allowed = await staff.Http.GetAsync("/Admin/Product?brandId=" + seed.BrandId))
        {
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
            Assert.DoesNotContain("data-product-lookup=\"supplier\"", await allowed.Content.ReadAsStringAsync());
        }
        using (var malformed = await admin.Http.GetAsync("/Admin/Product/Search?dataIssue=bogus")) Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        var route = $"/Admin/Product/Search?lifecycle=all&supplierId={seed.SupplierId}&brandId={seed.BrandId}&baseUnitId={seed.UnitId}&pageSize=1&page=2";
        using (var response = await admin.Http.GetAsync(route))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.Contains("data-current-page=\"2\"", html); Assert.Contains("data-total-pages=\"2\"", html);
            Assert.Contains("supplierId", html); Assert.Contains("brandId", html); Assert.Contains("baseUnitId", html);
        }
        await using var db = app.Database.CreateTenantContext(store.StoreId);
        var repository = new ProductRepository(db);
        var matches = await repository.SearchCatalogAsync(store.StoreId, "sua tuoi", null, null, null, 1, 20, new(seed.SupplierId, seed.BrandId, seed.UnitId));
        Assert.Equal(2, matches.TotalItems);
        matches = await repository.SearchCatalogAsync(store.StoreId, null, null, true, true, 1, 20, new(seed.SupplierId, seed.BrandId, seed.UnitId));
        Assert.Equal(seed.ProductId, Assert.Single(matches.Items).Id);
        matches = await repository.SearchCatalogAsync(store.StoreId, null, null, null, null, 1, 20, new(seed.OtherSupplierId, seed.BrandId));
        Assert.Empty(matches.Items);
        matches = await repository.SearchCatalogAsync(store.StoreId, null, null, null, null, 1, 20, new(DataIssue: "no-brand"));
        Assert.Equal("filter-other", Assert.Single(matches.Items).Alias);
        foreach (var issue in new[] { "no-image", "no-barcode" })
        {
            matches = await repository.SearchCatalogAsync(store.StoreId, null, null, null, null, 1, 20, new(DataIssue: issue));
            Assert.Equal(2, matches.TotalItems); Assert.DoesNotContain(matches.Items, x => x.Id == seed.ProductId);
        }
    }
}
