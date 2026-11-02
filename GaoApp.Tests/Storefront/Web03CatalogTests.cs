using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Storefront;
using GaoApp.Infrastructure.Services.Storefront;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb03"), Trait("Category", "StorefrontWeb03")]
public sealed class Web03CatalogTests(Web01Fixture fixture)
{
    [Fact]
    public async Task Featured_name_sort_group_filter_and_pagination_use_merchant_data_without_duplicates()
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync();
        var b = await c.AddProductAsync("WEB03 B", "Nhóm A", 20); var a = await c.AddProductAsync("WEB03 A", "Nhóm A", 30); var last = await c.AddProductAsync("WEB03 C", "Nhóm B", 10);
        await using var db = c.Context(); var catalog = new StorefrontCatalogService(db);
        Assert.Equal(new[] { last.ProductId, b.ProductId, a.ProductId }, (await catalog.SearchAsync(new())).Items.Select(x => x.ProductId));
        Assert.Equal(new[] { a.ProductId, b.ProductId, last.ProductId }, (await catalog.SearchAsync(new() { Sort = StorefrontCatalogSort.NameAsc })).Items.Select(x => x.ProductId));
        Assert.Equal(new[] { last.ProductId, b.ProductId, a.ProductId }, (await catalog.SearchAsync(new() { Sort = StorefrontCatalogSort.NameDesc })).Items.Select(x => x.ProductId));
        Assert.Equal(new[] { "Nhóm A", "Nhóm B" }, await catalog.GetGroupsAsync());
        Assert.Equal(new[] { b.ProductId, a.ProductId }, (await catalog.SearchAsync(new() { DisplayGroup = "Nhóm A" })).Items.Select(x => x.ProductId));
        var ids = new List<int>(); for (var page = 1; page <= 3; page++) { var result = await catalog.SearchAsync(new() { Page = page, PageSize = 1 }); Assert.Equal(3, result.TotalCount); ids.Add(Assert.Single(result.Items).ProductId); }
        Assert.Equal(3, ids.Distinct().Count()); Assert.Equal(new[] { last.ProductId, b.ProductId, a.ProductId }, ids);
    }
    [Theory]
    [InlineData("binh giu nhiet")][InlineData("BÌNH GIỮ NHIỆT")][InlineData("Bình giữ nhiệt")]
    public async Task Vietnamese_search_matches_current_public_names_with_or_without_diacritics(string term)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); var product = await c.AddProductAsync("Bình giữ nhiệt WEB03");
        await using var db = c.Context(); var found = await new StorefrontCatalogService(db).SearchAsync(new() { Search = term }); Assert.Equal(product.ProductId, Assert.Single(found.Items).ProductId);
    }
    [Fact]
    public async Task Groups_and_media_resolve_disappear_immediately_after_unpublish_and_never_reveal_foreign_storage()
    {
        var c = await fixture.CaseAsync(); var foreign = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync();
        var asset = await c.AssetAsync("uploads/products/03.png", "image/png"); var foreignAsset = await foreign.AssetAsync("uploads/products/private03.png", "image/png");
        await using var db = c.Context(); var request = c.Publication(); request.Media = [new() { MediaAssetId = asset, Kind = GaoApp.Domain.Storefront.StorefrontMediaKind.Image }];
        await c.Products(db).SaveAsync(c.ProductId, request); var catalog = new StorefrontCatalogService(db);
        Assert.Single(await catalog.GetGroupsAsync()); Assert.NotNull(await catalog.GetMediaAsync(asset)); Assert.Null(await catalog.GetMediaAsync(foreignAsset));
        await db.StorefrontProducts.ExecuteUpdateAsync(x => x.SetProperty(p => p.IsPublished, false));
        Assert.Empty(await catalog.GetGroupsAsync()); Assert.Null(await catalog.GetMediaAsync(asset)); Assert.Null(await catalog.GetAsync(c.ProductId));
    }
    [Theory]
    [InlineData("model")][InlineData("temporary")][InlineData("deleted")][InlineData("expiry")]
    public async Task Media_source_rechecks_lifecycle_after_publication(string change)
    {
        var c = await fixture.CaseAsync(); await c.EnableSyntheticCatalogAsync(); var asset = await c.AssetAsync("uploads/products/03.png", "image/png");
        await using var db = c.Context(); var request = c.Publication(); request.Media = [new() { MediaAssetId = asset, Kind = GaoApp.Domain.Storefront.StorefrontMediaKind.Image }]; await c.Products(db).SaveAsync(c.ProductId, request);
        var catalog = new StorefrontCatalogService(db); Assert.NotNull(await catalog.GetMediaAsync(asset));
        switch (change)
        {
            case "model": await db.MediaAssets.Where(x => x.Id == asset).ExecuteUpdateAsync(x => x.SetProperty(a => a.ContentType, "text/html")); break;
            case "temporary": await db.MediaAssets.Where(x => x.Id == asset).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsTemp, true)); break;
            case "deleted": await db.MediaAssets.Where(x => x.Id == asset).ExecuteUpdateAsync(x => x.SetProperty(a => a.IsDeleted, true)); break;
            case "expiry": await db.MediaAssets.Where(x => x.Id == asset).ExecuteUpdateAsync(x => x.SetProperty(a => a.ExpireAtUtc, DateTime.UtcNow.AddDays(1))); break;
        }
        Assert.Null(await catalog.GetMediaAsync(asset));
    }
}
