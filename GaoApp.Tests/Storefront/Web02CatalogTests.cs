using GaoApp.Application.Services.Products;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02CatalogTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Pack_only_is_explicit_without_fake_base_conversion_or_duplicate_base_choice()
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync();
        await using var db = c.Context(); var service = c.Products(db); var catalog = new StorefrontCatalogService(db);
        var count = await db.ProductUnitConversions.CountAsync();
        await service.SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion));
        var product = Assert.IsType<StorefrontCatalogProduct>(await catalog.GetAsync(c.ProductId));
        var unit = Assert.Single(Assert.Single(product.Variants).Units); Assert.Equal(pack.Unit, unit.UnitId); Assert.Equal(pack.Conversion, unit.ProductUnitConversionId);
        Assert.Null(await catalog.ResolveAsync(c.VariantId, c.UnitId)); Assert.NotNull(await catalog.ResolveAsync(c.VariantId, pack.Unit));
        Assert.Equal(count, await db.ProductUnitConversions.CountAsync());
        var request = await c.SavedAsync(); request.Variants[0].BaseUnitIsPublished = true; await service.SaveAsync(c.ProductId, request);
        var options = Assert.Single((await catalog.GetAsync(c.ProductId))!.Variants).Units;
        Assert.Equal(2, options.Count); Assert.Single(options, x => x.UnitId == c.UnitId); Assert.Equal(c.ConversionId, options.Single(x => x.UnitId == c.UnitId).ProductUnitConversionId);
        Assert.Empty(await db.StorefrontSellingUnits.Where(x => x.ProductUnitConversionId == c.ConversionId).ToListAsync());
    }

    [Theory]
    [InlineData(25000, 30000d, 40000d, 40000)]
    [InlineData(25000, 30000d, 0d, 30000)]
    [InlineData(25000, 30000d, -1d, 30000)]
    [InlineData(25000, 0d, 0d, 25000)]
    [InlineData(25000, -1d, -1d, 25000)]
    [InlineData(25000, null, null, 25000)]
    [InlineData(0, 0d, 0d, 0)]
    public async Task Base_and_pack_retail_price_match_positive_POS_overrides_without_pack_multiplication(decimal basePrice, double? variantPrice, double? overridePrice, decimal expected)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(factor: 12, price: overridePrice.HasValue ? (decimal)overridePrice.Value : null);
        await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        var variant = await db.ProductVariants.Include(x => x.Product).SingleAsync(x => x.Id == c.VariantId);
        variant.Price = variantPrice.HasValue ? (decimal)variantPrice.Value : null; variant.WholesalePrice = 2; variant.Product.BasePrice = basePrice;
        var baseConversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == c.ConversionId);
        baseConversion.Price = overridePrice.HasValue ? (decimal)overridePrice.Value : null; baseConversion.WholesalePrice = 1;
        await db.SaveChangesAsync(); await c.Products(db).SaveAsync(c.ProductId, c.Publication(pack: pack.Conversion));
        var catalog = new StorefrontCatalogService(db); var baseChoice = Assert.IsType<StorefrontCatalogUnit>(await catalog.ResolveAsync(c.VariantId, c.UnitId));
        var packChoice = Assert.IsType<StorefrontCatalogUnit>(await catalog.ResolveAsync(c.VariantId, pack.Unit));
        var packConversion = await db.ProductUnitConversions.SingleAsync(x => x.Id == pack.Conversion);
        Assert.Equal(expected, baseChoice.RetailPrice); Assert.Equal(expected, packChoice.RetailPrice);
        Assert.Equal(ProductSalePricePolicy.Pos(variant, baseConversion, wholesale: false), baseChoice.RetailPrice);
        Assert.Equal(ProductSalePricePolicy.Pos(variant, packConversion, wholesale: false), packChoice.RetailPrice);
        Assert.Equal(1, baseChoice.BaseMultiplier); Assert.Equal(12, packChoice.BaseMultiplier);
        var page = await c.Products(db).GetAsync(c.ProductId); var admin = Assert.Single(page.VariantOptions);
        Assert.Equal(expected, admin.BaseRetailPrice); Assert.Equal(expected, Assert.Single(admin.Units, x => x.ConversionId == pack.Conversion).RetailPrice);
    }

    [Theory]
    [InlineData("publication")][InlineData("variantPublication")][InlineData("baseChoice")]
    [InlineData("productInactive")][InlineData("productUnsellable")][InlineData("productDeleted")]
    [InlineData("variantInactive")][InlineData("variantDeleted")][InlineData("baseUnitInactive")]
    [InlineData("storeInactive")][InlineData("settingsClosed")]
    public async Task All_public_read_paths_recheck_the_entire_source_and_publication_chain_immediately(string change)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication()); var catalog = new StorefrontCatalogService(db);
        Assert.Equal(1, (await catalog.SearchAsync(new())).TotalCount); Assert.NotNull(await catalog.GetAsync(c.ProductId)); Assert.NotNull(await catalog.ResolveAsync(c.VariantId, c.UnitId));
        await using (var mutate = c.Context())
        {
            switch (change)
            {
                case "publication": await mutate.StorefrontProducts.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false)); break;
                case "variantPublication": await mutate.StorefrontVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false)); break;
                case "baseChoice": await mutate.StorefrontVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.BaseUnitIsPublished, false)); break;
                case "productInactive": await mutate.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "productUnsellable": await mutate.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsSellable, false)); break;
                case "productDeleted": await mutate.Products.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); break;
                case "variantInactive": await mutate.ProductVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "variantDeleted": await mutate.ProductVariants.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); break;
                case "baseUnitInactive": await mutate.Units.Where(x => x.Id == c.UnitId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "storeInactive": await mutate.Stores.Where(x => x.Id == c.StoreId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "settingsClosed": await mutate.StorefrontSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsEnabled, false)); break;
            }
        }
        var search = await catalog.SearchAsync(new()); Assert.Equal(0, search.TotalCount); Assert.Empty(search.Items);
        Assert.Null(await catalog.GetAsync(c.ProductId)); Assert.Null(await catalog.ResolveAsync(c.VariantId, c.UnitId));
    }

    [Theory]
    [InlineData("unitHidden")][InlineData("conversionInactive")][InlineData("conversionDeleted")][InlineData("sellingUnitInactive")][InlineData("zeroFactor")]
    public async Task Pack_source_or_selection_becomes_invalid_without_hiding_valid_base_choice(string change)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(pack: pack.Conversion)); var catalog = new StorefrontCatalogService(db);
        Assert.NotNull(await catalog.ResolveAsync(c.VariantId, pack.Unit));
        await using (var mutate = c.Context())
        {
            switch (change)
            {
                case "unitHidden": await mutate.StorefrontSellingUnits.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsPublished, false)); break;
                case "conversionInactive": await mutate.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "conversionDeleted": await mutate.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsDeleted, true)); break;
                case "sellingUnitInactive": await mutate.Units.Where(x => x.Id == pack.Unit).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false)); break;
                case "zeroFactor": await mutate.ProductUnitConversions.Where(x => x.Id == pack.Conversion).ExecuteUpdateAsync(s => s.SetProperty(x => x.Factor, 0)); break;
            }
        }
        Assert.Null(await catalog.ResolveAsync(c.VariantId, pack.Unit)); Assert.NotNull(await catalog.ResolveAsync(c.VariantId, c.UnitId));
        Assert.Single(Assert.Single((await catalog.GetAsync(c.ProductId))!.Variants).Units);
        Assert.Equal(1, (await catalog.SearchAsync(new())).TotalCount);
    }

    [Fact]
    public async Task Foreign_tenant_and_host_context_cannot_resolve_same_ids_or_leak_catalog()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync();
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, c.Publication());
        await using var foreign = other.Context(); await using var host = fixture.Database.CreateHostContext();
        foreach (var db in new[] { foreign, host })
        {
            var catalog = new StorefrontCatalogService(db); Assert.Null(await catalog.GetAsync(c.ProductId));
            Assert.Null(await catalog.ResolveAsync(c.VariantId, c.UnitId)); Assert.Empty((await catalog.SearchAsync(new())).Items);
        }
    }

    [Fact]
    public async Task Search_group_and_display_content_use_current_publication_data()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication()); var catalog = new StorefrontCatalogService(db);
        var found = Assert.Single((await catalog.SearchAsync(new() { Search = "binh giu nhiet", DisplayGroup = "Gia dụng" })).Items);
        Assert.Equal(c.ProductId, found.ProductId); Assert.Equal("Bình giữ nhiệt WEB02", found.Name); Assert.Contains("tiếng Việt", found.Description);
        Assert.Empty((await catalog.SearchAsync(new() { DisplayGroup = "Khác" })).Items);
        Assert.Empty((await catalog.SearchAsync(new() { Page = 2, PageSize = 1 })).Items);
    }
}
