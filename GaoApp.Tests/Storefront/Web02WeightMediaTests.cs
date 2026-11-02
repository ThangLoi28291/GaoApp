using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02WeightMediaTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("baseMissing")][InlineData("packMissing")][InlineData("baseZero")][InlineData("packZero")]
    [InlineData("baseNegative")][InlineData("packNegative")][InlineData("basePrecision")][InlineData("packPrecision")]
    [InlineData("baseOverflow")][InlineData("packOverflow")]
    public async Task Weight_mode_rejects_missing_invalid_or_unrepresentable_kg_for_each_selected_selling_unit(string invalid)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync(byWeight: true);
        var r = c.Publication(pack: pack.Conversion);
        switch (invalid)
        {
            case "baseMissing": r.Variants[0].BaseUnitWeightKg = null; break;
            case "packMissing": r.Variants[0].Units[0].WeightKg = null; break;
            case "baseZero": r.Variants[0].BaseUnitWeightKg = 0; break;
            case "packZero": r.Variants[0].Units[0].WeightKg = 0; break;
            case "baseNegative": r.Variants[0].BaseUnitWeightKg = -1; break;
            case "packNegative": r.Variants[0].Units[0].WeightKg = -1; break;
            case "basePrecision": r.Variants[0].BaseUnitWeightKg = 0.1234567m; break;
            case "packPrecision": r.Variants[0].Units[0].WeightKg = 0.1234567m; break;
            case "baseOverflow": r.Variants[0].BaseUnitWeightKg = 1_000_000_000_000m; break;
            case "packOverflow": r.Variants[0].Units[0].WeightKg = 1_000_000_000_000m; break;
        }
        await using var db = c.Context(); await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, r));
        await using var verify = c.Context(); Assert.Empty(await verify.StorefrontProducts.ToListAsync());
    }

    [Fact]
    public async Task Fixed_fee_accepts_missing_weight_and_pack_only_kg_is_not_multiplied_or_inherited()
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion, baseKg: null, packKg: null));
        var fixedChoice = await new StorefrontCatalogService(db).ResolveAsync(c.VariantId, pack.Unit); Assert.NotNull(fixedChoice); Assert.Null(fixedChoice.WeightKg);
        var request = await c.SavedAsync(); request.Variants[0].Units[0].WeightKg = 1.123456m;
        await c.Products(db).SaveAsync(c.ProductId, request);
        // Source configuration enable is fixture-only. This isolates current catalog behavior.
        await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.ShippingByWeight, true));
        var catalog = new StorefrontCatalogService(db); var choice = Assert.IsType<StorefrontCatalogUnit>(await catalog.ResolveAsync(c.VariantId, pack.Unit));
        Assert.Equal(1.123456m, choice.WeightKg); Assert.Equal(12, choice.BaseMultiplier); Assert.Null(await catalog.ResolveAsync(c.VariantId, c.UnitId));
        await db.StorefrontSellingUnits.ExecuteUpdateAsync(x => x.SetProperty(s => s.WeightKg, (decimal?)null));
        Assert.Null(await catalog.ResolveAsync(c.VariantId, pack.Unit)); Assert.Equal(0, (await catalog.SearchAsync(new())).TotalCount);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Switching_to_kg_checks_only_selected_base_or_pack_and_keeps_service_fee(bool complete)
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(baseUnit: false, pack: pack.Conversion, baseKg: null, packKg: complete ? 2.2m : null));
        var settings = (await c.FullService(db).GetAsync()).Settings; settings.ShippingByWeight = true;
        if (complete)
        {
            await c.FullService(db).SaveAsync(settings);
            Assert.True((await c.FullService(db).GetAsync()).Settings.ShippingByWeight);
        }
        else await Assert.ThrowsAsync<ValidationAppException>(() => c.FullService(db).SaveAsync(settings));
        await using var verify = c.Context(); Assert.Equal(StorefrontShippingFeeTreatment.StoreServiceFee, (await verify.StorefrontSettings.SingleAsync()).ShippingFeeTreatment);
        Assert.Equal(complete, (await verify.StorefrontSettings.SingleAsync()).ShippingByWeight);
    }

    [Fact]
    public async Task Publishing_after_fixed_mode_configuration_is_revalidated_when_weight_mode_changes()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); await using var db = c.Context(); var service = c.Products(db);
        await service.SaveAsync(c.ProductId, c.Publication(publish: false, baseKg: null)); var saved = await c.SavedAsync();
        await db.StorefrontSettings.ExecuteUpdateAsync(x => x.SetProperty(s => s.ShippingByWeight, true));
        Assert.Equal(StorefrontPublicationOutcome.Invalid, (await service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = true, Version = saved.Version })).Outcome);
        await using var verify = c.Context(); Assert.False((await verify.StorefrontProducts.SingleAsync()).IsPublished);
    }

    [Fact]
    public async Task Existing_same_store_library_assets_roundtrip_and_public_content_stays_plain_text()
    {
        var c = await fixture.CaseAsync(); var image = await c.AssetAsync(); var model = await c.AssetAsync("uploads/products/web02.glb", "model/gltf-binary");
        await c.EnableSyntheticCatalogAsync(); var r = c.Publication(); r.Description = "<script>alert('x')</script> & nội dung";
        r.Media = [new() { MediaAssetId = model, Kind = StorefrontMediaKind.Model3D, AltText = "Mô hình", SortOrder = 2 },
            new() { MediaAssetId = image, Kind = StorefrontMediaKind.Image, AltText = "Ảnh \"sản phẩm\"", SortOrder = 1 }];
        await using var db = c.Context(); var assetsBefore = await db.MediaAssets.CountAsync(); await c.Products(db).SaveAsync(c.ProductId, r);
        var saved = await c.SavedAsync(); Assert.Equal(r.Description, saved.Description); Assert.Equal(2, saved.Media.Count);
        var product = Assert.IsType<StorefrontCatalogProduct>(await new StorefrontCatalogService(db).GetAsync(c.ProductId));
        Assert.Equal(r.Description, product.Description); Assert.Equal(new[] { image, model }, product.Media.Select(x => x.MediaAssetId));
        Assert.Equal("/shop/media/" + image, product.Media[0].Path); Assert.Equal("Ảnh \"sản phẩm\"", product.Media[0].AltText);
        Assert.Equal(assetsBefore, await db.MediaAssets.CountAsync());
        Assert.Null(typeof(StorefrontMediaRequest).GetProperty("Path")); Assert.Null(typeof(StorefrontMediaRequest).GetProperty("StoragePath")); Assert.Null(typeof(StorefrontMediaRequest).GetProperty("StoreId"));
    }

    [Theory]
    [InlineData("foreign")][InlineData("temporary")][InlineData("token")][InlineData("expiry")][InlineData("deleted")]
    [InlineData("external")][InlineData("traversal")][InlineData("encodedTraversal")][InlineData("tempFolder")]
    [InlineData("svg")][InlineData("html")][InlineData("wrongMime")][InlineData("wrongKind")][InlineData("unknownKind")][InlineData("longAlt")]
    public async Task Media_selection_rejects_foreign_temporary_deleted_unsafe_or_mismatched_assets(string kind)
    {
        var c = await fixture.CaseAsync(); var r = c.Publication(); int asset;
        if (kind == "foreign") asset = await (await fixture.CaseAsync()).AssetAsync();
        else asset = await c.AssetAsync(
            path: kind switch { "external" => "https://example.test/a.jpg", "traversal" => "uploads/../a.jpg", "encodedTraversal" => "uploads/%2e%2e/a.jpg",
                "tempFolder" => "uploads/_TEMP/a.jpg", "svg" => "uploads/a.svg", "html" => "uploads/a.html", _ => "uploads/products/a.jpg" },
            type: kind == "wrongMime" ? "text/html" : "image/jpeg", temp: kind is "temporary" or "expiry", token: kind == "token" ? "not-final" : null,
            expires: kind == "expiry" ? DateTime.UtcNow.AddHours(1) : null, deleted: kind == "deleted");
        r.Media = [new() { MediaAssetId = asset, Kind = kind == "wrongKind" ? StorefrontMediaKind.Model3D : kind == "unknownKind" ? (StorefrontMediaKind)99 : StorefrontMediaKind.Image,
            AltText = kind == "longAlt" ? new string('a', 301) : "Synthetic" }];
        await using var db = c.Context(); await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, r));
        await using var verify = c.Context(); Assert.Empty(await verify.StorefrontProducts.ToListAsync()); Assert.Empty(await verify.Set<StorefrontMedia>().ToListAsync());
    }

    [Fact]
    public async Task Media_search_never_returns_foreign_temporary_deleted_or_unsafe_asset_and_public_read_rechecks_lifecycle()
    {
        var c = await fixture.CaseAsync(); var image = await c.AssetAsync(); var temp = await c.AssetAsync(temp: true);
        var deleted = await c.AssetAsync(deleted: true); var foreign = await (await fixture.CaseAsync()).AssetAsync(); var unsafeAsset = await c.AssetAsync("uploads/../a.jpg");
        await using var db = c.Context(); var options = await c.Products(db).SearchMediaAsync("WEB02");
        Assert.Contains(options, x => x.Id == image); Assert.DoesNotContain(options, x => new[] { temp, deleted, foreign, unsafeAsset }.Contains(x.Id));
        await c.EnableSyntheticCatalogAsync(); var r = c.Publication(); r.Media = [new() { MediaAssetId = image, Kind = StorefrontMediaKind.Image }];
        await c.Products(db).SaveAsync(c.ProductId, r); var catalog = new StorefrontCatalogService(db); Assert.Single((await catalog.GetAsync(c.ProductId))!.Media);
        await db.MediaAssets.Where(x => x.Id == image).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsTemp, true));
        Assert.Empty((await catalog.GetAsync(c.ProductId))!.Media);
    }

    [Fact]
    public async Task Hidden_parent_draft_does_not_block_enabling_kg_but_cannot_be_republished_until_weight_is_supplied()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context(); var products = c.Products(db);
        await products.SaveAsync(c.ProductId, c.Publication(publish: false, baseKg: null));
        var settings = (await c.FullService(db).GetAsync()).Settings; settings.ShippingByWeight = true; await c.FullService(db).SaveAsync(settings);
        Assert.True((await c.FullService(db).GetAsync()).Settings.ShippingByWeight);
        var draft = await c.SavedAsync(); draft.Description = "Draft may stay incomplete while hidden"; await products.SaveAsync(c.ProductId, draft);
        draft = await c.SavedAsync();
        Assert.Equal(StorefrontPublicationOutcome.Invalid, (await products.SetPublicationAsync(new() { ProductId = c.ProductId, Version = draft.Version, IsPublished = true })).Outcome);
        draft.Variants[0].BaseUnitWeightKg = 0.25m; await products.SaveAsync(c.ProductId, draft); draft = await c.SavedAsync();
        Assert.Equal(StorefrontPublicationOutcome.Updated, (await products.SetPublicationAsync(new() { ProductId = c.ProductId, Version = draft.Version, IsPublished = true })).Outcome);
    }

    [Fact]
    public async Task Hidden_variant_with_retained_unit_flags_does_not_block_kg_and_reactivation_revalidates_each_pack()
    {
        var c = await fixture.CaseAsync(); var pack = await c.PackAsync(); await c.SaveDefaultsAsync(); await using var db = c.Context();
        await c.Products(db).SaveAsync(c.ProductId, c.Publication(pack: pack.Conversion, packKg: null));
        // Existing persisted selection may remain when a parent gate is disabled.
        await db.StorefrontVariants.ExecuteUpdateAsync(x => x.SetProperty(v => v.IsPublished, false));
        var settings = (await c.FullService(db).GetAsync()).Settings; settings.ShippingByWeight = true; await c.FullService(db).SaveAsync(settings);
        var request = await c.SavedAsync(); request.Variants[0].IsPublished = true;
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, request));
        request.Variants[0].Units[0].WeightKg = 1.5m; await c.Products(db).SaveAsync(c.ProductId, request);
    }

    [Fact]
    public async Task Racing_weight_enable_and_weightless_publication_never_commit_both()
    {
        var c = await fixture.CaseAsync(); await c.SaveDefaultsAsync();
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, c.Publication(publish: false, baseKg: null));
        var publication = await c.SavedAsync(); StorefrontSettingsRequest settings;
        await using (var db = c.Context()) settings = (await c.FullService(db).GetAsync()).Settings;
        settings.ShippingByWeight = true;
        async Task<bool> EnableWeight()
        {
            await using var db = c.Context();
            try { await c.FullService(db).SaveAsync(settings); return true; } catch (ValidationAppException) { return false; }
        }
        async Task<bool> Publish()
        {
            await using var db = c.Context(); return (await c.Products(db).SetPublicationAsync(new() { ProductId = c.ProductId, Version = publication.Version, IsPublished = true })).Outcome == StorefrontPublicationOutcome.Updated;
        }
        var outcomes = await Task.WhenAll(EnableWeight(), Publish()); Assert.Single(outcomes, x => x);
        await using var verify = c.Context(); Assert.False((await verify.StorefrontSettings.SingleAsync()).ShippingByWeight && (await verify.StorefrontProducts.SingleAsync()).IsPublished);
    }
}
