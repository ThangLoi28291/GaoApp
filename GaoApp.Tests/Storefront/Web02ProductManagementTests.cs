using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb02"), Trait("Category", "StorefrontWeb02")]
public sealed class Web02ProductManagementTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Reads_leave_catalog_unconfigured_and_unpublished_without_database_writes()
    {
        var c = await fixture.CaseAsync(); var before = await c.SideEffectsAsync(); await using var db = c.Context();
        var service = c.Products(db); var page = await service.ListAsync(new()); var product = await service.GetAsync(c.ProductId);
        var item = Assert.Single(page.Items); Assert.Equal(c.ProductId, item.ProductId); Assert.False(item.IsConfigured); Assert.False(item.IsPublished);
        Assert.False(product.Product.IsPublished); Assert.Equal("", product.Product.Version); Assert.True(product.CanManage);
        Assert.All(product.Product.Variants, v => { Assert.False(v.IsPublished); Assert.False(v.BaseUnitIsPublished); Assert.All(v.Units, u => Assert.False(u.IsPublished)); });
        Assert.Single(product.VariantOptions); Assert.Empty(await db.StorefrontProducts.ToListAsync()); Assert.Empty(await db.StorefrontVariants.ToListAsync());
        Assert.Empty(await db.StorefrontSellingUnits.ToListAsync()); Assert.Empty(await db.StorefrontSettings.ToListAsync()); Assert.Equal(before, await c.SideEffectsAsync());
    }

    [Fact]
    public async Task Manual_save_roundtrips_website_fields_without_changing_source_or_accepted_snapshots()
    {
        var c = await fixture.CaseAsync(); await c.CommerceAsync(); var before = await c.SideEffectsAsync();
        await using (var db = c.Context()) await c.Products(db).SaveAsync(c.ProductId, c.Publication());
        var loaded = await c.SavedAsync(); Assert.True(loaded.IsPublished); Assert.Equal("Bình giữ nhiệt WEB02", loaded.DisplayName);
        Assert.Equal("Mô tả tiếng Việt\nDòng thứ hai.", loaded.Description); Assert.Equal("Gia dụng", loaded.DisplayGroup); Assert.Equal(17, loaded.SortOrder);
        Assert.Equal(8, Convert.FromBase64String(loaded.Version).Length); Assert.True(Assert.Single(loaded.Variants).BaseUnitIsPublished);
        await using (var db = c.Context())
        {
            var result = await c.Products(db).SetPublicationAsync(new() { ProductId = c.ProductId, Version = loaded.Version, IsPublished = false });
            Assert.Equal(StorefrontPublicationOutcome.Updated, result.Outcome);
        }
        Assert.Equal(before, await c.SideEffectsAsync());
    }

    [Fact]
    public async Task Admin_search_and_status_filters_are_same_store_and_paged()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync();
        await using var db = c.Context(); var service = c.Products(db);
        Assert.Single((await service.ListAsync(new() { Publication = StorefrontPublicationFilter.Unconfigured })).Items);
        await service.SaveAsync(c.ProductId, c.Publication(publish: false));
        Assert.Empty((await service.ListAsync(new() { Publication = StorefrontPublicationFilter.Unconfigured })).Items);
        Assert.Single((await service.ListAsync(new() { Publication = StorefrontPublicationFilter.Hidden, Search = "Bình giữ nhiệt WEB02", PageSize = 1 })).Items);
        Assert.Empty((await service.ListAsync(new() { Publication = StorefrontPublicationFilter.Published })).Items);
        var current = await c.SavedAsync();
        Assert.Equal(StorefrontPublicationOutcome.Updated, (await service.SetPublicationAsync(new() { ProductId = c.ProductId, Version = current.Version, IsPublished = true })).Outcome);
        var list = await service.ListAsync(new() { Publication = StorefrontPublicationFilter.Published }); Assert.Equal(1, list.TotalCount); Assert.Equal(c.ProductId, Assert.Single(list.Items).ProductId);
        Assert.DoesNotContain((await service.ListAsync(new())).Items, x => x.ProductId == other.ProductId);
        Assert.Empty((await service.ListAsync(new() { Page = 2, PageSize = 1 })).Items);
        string sku = await db.ProductVariants.Where(x => x.Id == c.VariantId).Select(x => x.Sku).SingleAsync();
        Assert.Equal(c.ProductId, Assert.Single((await service.ListAsync(new() { Search = sku })).Items).ProductId);
    }

    [Fact]
    public async Task Bulk_reports_valid_foreign_missing_and_stale_items_without_silent_success()
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync();
        await using var db = c.Context(); var service = c.Products(db); await service.SaveAsync(c.ProductId, c.Publication(publish: false));
        var current = await c.SavedAsync(); var result = await service.SetPublicationBulkAsync(new() { Items = [
            new() { ProductId = c.ProductId, Version = current.Version, IsPublished = true },
            new() { ProductId = other.ProductId, IsPublished = true }, new() { ProductId = int.MaxValue, IsPublished = true }] });
        Assert.Equal(3, result.Count); Assert.Equal(StorefrontPublicationOutcome.Updated, result.Single(x => x.ProductId == c.ProductId).Outcome);
        Assert.Equal(StorefrontPublicationOutcome.NotFound, result.Single(x => x.ProductId == other.ProductId).Outcome);
        Assert.Equal(StorefrontPublicationOutcome.NotFound, result.Single(x => x.ProductId == int.MaxValue).Outcome);
        Assert.All(result, x => Assert.False(string.IsNullOrWhiteSpace(x.Message)));
        Assert.Equal(StorefrontPublicationOutcome.Conflict, (await service.SetPublicationAsync(new() { ProductId = c.ProductId, Version = current.Version, IsPublished = false })).Outcome);
        await using var foreign = other.Context(); Assert.Empty(await foreign.StorefrontProducts.ToListAsync());
    }

    [Theory]
    [InlineData("display")][InlineData("description")][InlineData("group")][InlineData("duplicateVariant")]
    [InlineData("foreignVariant")][InlineData("foreignConversion")][InlineData("baseConversion")]
    [InlineData("duplicateConversion")][InlineData("nullVariants")][InlineData("nullUnits")][InlineData("nullMedia")]
    [InlineData("tooManyVariants")][InlineData("tooManyUnits")][InlineData("tooManyMedia")]
    public async Task Invalid_configuration_is_rejected_atomically(string kind)
    {
        var c = await fixture.CaseAsync(); var other = await fixture.CaseAsync(); var pack = await c.PackAsync(); var r = c.Publication(pack: pack.Conversion);
        switch (kind)
        {
            case "display": r.DisplayName = new string('x', 201); break;
            case "description": r.Description = new string('x', 6001); break;
            case "group": r.DisplayGroup = new string('x', 101); break;
            case "duplicateVariant": r.Variants.Add(r.Variants[0]); break;
            case "foreignVariant": r.Variants[0].VariantId = other.VariantId; break;
            case "foreignConversion": r.Variants[0].Units[0].ProductUnitConversionId = other.ConversionId; break;
            case "baseConversion": r.Variants[0].Units[0].ProductUnitConversionId = c.ConversionId; break;
            case "duplicateConversion": r.Variants[0].Units.Add(r.Variants[0].Units[0]); break;
            case "nullVariants": r.Variants = null!; break;
            case "nullUnits": r.Variants[0].Units = null!; break;
            case "nullMedia": r.Media = null!; break;
            case "tooManyVariants": r.Variants = Enumerable.Range(0, 201).Select(_ => new StorefrontVariantRequest { VariantId = c.VariantId }).ToList(); break;
            case "tooManyUnits": r.Variants[0].Units = Enumerable.Range(0, 101).Select(_ => new StorefrontSellingUnitRequest { ProductUnitConversionId = pack.Conversion }).ToList(); break;
            case "tooManyMedia": r.Media = Enumerable.Range(0, 21).Select(_ => new StorefrontMediaRequest { MediaAssetId = 1 }).ToList(); break;
        }
        await using var db = c.Context(); await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).SaveAsync(c.ProductId, r));
        await using var verify = c.Context(); Assert.Empty(await verify.StorefrontProducts.ToListAsync()); Assert.Empty(await verify.StorefrontVariants.ToListAsync());
        Assert.Empty(await verify.StorefrontSellingUnits.ToListAsync());
    }

    [Theory]
    [InlineData(0, 25)][InlineData(1, 0)][InlineData(1, 101)][InlineData(1000001, 1)]
    public async Task Invalid_page_bounds_are_rejected(int page, int size)
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context();
        await Assert.ThrowsAsync<ValidationAppException>(() => c.Products(db).ListAsync(new() { Page = page, PageSize = size }));
    }

    [Fact]
    public async Task Bulk_rejects_duplicate_identity_and_over_limit()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var service = c.Products(db);
        await Assert.ThrowsAsync<ValidationAppException>(() => service.SetPublicationBulkAsync(new() { Items = [new() { ProductId = c.ProductId }, new() { ProductId = c.ProductId }] }));
        await Assert.ThrowsAsync<ValidationAppException>(() => service.SetPublicationBulkAsync(new() { Items = Enumerable.Range(1, 101).Select(id => new StorefrontPublicationRequest { ProductId = id }).ToList() }));
        Assert.Empty(await db.StorefrontProducts.ToListAsync());
    }

    [Fact]
    public async Task Unconfigured_hide_does_not_insert_and_publish_requires_manual_choice_then_retry_is_harmless()
    {
        var c = await fixture.CaseAsync(); await using var db = c.Context(); var service = c.Products(db);
        Assert.Equal(StorefrontPublicationOutcome.Unchanged, (await service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = false })).Outcome);
        Assert.Empty(await db.StorefrontProducts.ToListAsync());
        Assert.Equal(StorefrontPublicationOutcome.Invalid, (await service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = true })).Outcome);
        Assert.Empty(await db.StorefrontProducts.ToListAsync());
        await service.SaveAsync(c.ProductId, c.Publication(publish: false)); var saved = await c.SavedAsync();
        var first = await service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = true, Version = saved.Version });
        var retry = await service.SetPublicationAsync(new() { ProductId = c.ProductId, IsPublished = true, Version = saved.Version });
        Assert.Equal(StorefrontPublicationOutcome.Updated, first.Outcome); Assert.Equal(StorefrontPublicationOutcome.Unchanged, retry.Outcome);
        Assert.Equal(first.Version, retry.Version); Assert.Single(await db.StorefrontProducts.ToListAsync());
    }
}
