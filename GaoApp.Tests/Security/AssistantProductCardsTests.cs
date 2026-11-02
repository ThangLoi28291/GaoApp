using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed partial class AssistantIntegrationTests
{
    private static JsonElement DisplayHistory(AssistantIdentity who, CatalogProduct product, string kind = "quote", string[]? tiers = null, string messageId = "12") => JsonSerializer.SerializeToElement(new {
        items = new[] { new { messageId = "12", role = "assistant" } },
        productDisplay = new { messageId, who.Scope, kind, items = new[] { new { product, choice = kind == "choices" ? (int?)1 : null } },
            expiresAt = DateTimeOffset.UtcNow.AddMinutes(30), hasMore = false,
            priceReadAt = kind == "quote" ? (DateTimeOffset?)DateTimeOffset.UtcNow : null, priceTiers = kind == "quote" ? tiers ?? ["retail"] : null }
    }, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task CardsUseCurrentScopedSkuAttributesPhotoAndKeepOriginalPriceTime()
    {
        using var f = new Fixture(); await f.Seed(); var who = await f.Access.CurrentAsync(null, default);
        var variant = await f.Db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Id == 10);
        var media = new MediaAsset { Id = 501, StoreId = 1, StoragePath = "uploads/products/store1/test.png", ContentType = "image/png", RowVersion = [1] };
        var image = new ProductImage { Id = 502, StoreId = 1, ProductId = variant.ProductId, MediaAssetId = media.Id, MediaAsset = media, IsPrimary = true, RowVersion = [1] };
        f.Db.ProductImages.Add(image); await f.Db.SaveChangesAsync();
        var service = new AssistantCatalogService(f.Db, f.Clock); var product = new CatalogProduct("10", "10", variant.Product.Name);
        var history = DisplayHistory(who, product); var result = await service.DisplayAsync(who, history, default);
        Assert.Equal("TH180-1", result.Display!["items"]![0]!["sku"]!.GetValue<string>());
        Assert.Equal("/uploads/products/store1/test.png", result.Display["items"]![0]!["imageUrl"]!.GetValue<string>());
        Assert.Equal("TH", result.Display["items"]![0]!["attributes"]!["brand"]!.GetValue<string>());
        Assert.Equal(history.GetProperty("productDisplay").GetProperty("priceReadAt").GetDateTimeOffset(), result.Display["priceReadAt"]!.GetValue<DateTimeOffset>());
        Assert.DoesNotContain("amount", result.Display.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CostPrice", result.Display.ToJsonString());
        media.IsTemp = true; await f.Db.SaveChangesAsync(); result = await service.DisplayAsync(who, history, default);
        Assert.Null(result.Display!["items"]![0]!["imageUrl"]);
    }
    [Theory]
    [InlineData("uploads/products/a.jpg", "/uploads/products/a.jpg")]
    [InlineData("uploads/products/ảnh 1.png", "/uploads/products/%E1%BA%A3nh%201.png")]
    [InlineData("https://other.invalid/a.png", null)]
    [InlineData("uploads/products/../secrets/a.png", null)]
    [InlineData("uploads/products/%2e%2e/a.png", null)]
    [InlineData("uploads/products/a.svg", null)]
    [InlineData("uploads/products/a.jpg?token=x", null)]
    [InlineData("uploads/products/a\\b.png", null)]
    [InlineData("/uploads/products/a.png", null)]
    public void ProductImagePathsStayLocalWithoutTraversalOrExternalUrls(string path, string? expected)
        => Assert.Equal(expected, AssistantCatalogService.SafeImagePath(path));

    [Fact]
    public async Task CardsRejectForeignScopeWrongProductAndMessageBindingAndNeverReadOtherStoreImages()
    {
        using var f = new Fixture(); await f.Seed(); var who = await f.Access.CurrentAsync(null, default);
        var service = new AssistantCatalogService(f.Db, f.Clock); var product = new CatalogProduct("10", "10", "Sữa tươi TH có đường hộp 180ml");
        var foreign = DisplayHistory(who with { Scope = new("2", null, null) }, product);
        Assert.Equal("source_scope_mismatch", (await Assert.ThrowsAsync<AssistantException>(() => service.DisplayAsync(who, foreign, default))).Code);
        Assert.Equal(502, (await Assert.ThrowsAsync<AssistantException>(() => service.DisplayAsync(who, DisplayHistory(who, product with { ProductId = "20" }), default))).Status);
        Assert.Equal(502, (await Assert.ThrowsAsync<AssistantException>(() => service.DisplayAsync(who, DisplayHistory(who, product, messageId: "11"), default))).Status);
        Assert.Null((await service.DisplayAsync(who, DisplayHistory(who, product with { ProductId = "20", VariantId = "20" }), default)).Display);
        Assert.Null((await service.DisplayAsync(who, DisplayHistory(who, product with { Name = "Tên đã đổi" }), default)).Display);
    }
    [Fact]
    public async Task CardsRequireCatalogAndQuotedTiersButCatalogOnlyCardsCannotExposePrices()
    {
        using var f = new Fixture(); await f.Seed(); var who = await f.Access.CurrentAsync(null, default);
        var service = new AssistantCatalogService(f.Db, f.Clock); var product = new CatalogProduct("10", "10", "Sữa tươi TH có đường hộp 180ml");
        var limited = who with { Permissions = [AssistantPermissions.Use, AssistantPermissions.Chat, AssistantPermissions.Catalog] };
        Assert.Equal(403, (await Assert.ThrowsAsync<AssistantException>(() => service.DisplayAsync(limited, DisplayHistory(who, product), default))).Status);
        var card = (await service.DisplayAsync(limited, DisplayHistory(who, product, "product"), default)).Display!;
        Assert.DoesNotContain("price", card["items"]!.ToJsonString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(card["priceReadAt"]); Assert.NotEmpty(card["items"]![0]!["saleUnits"]!.AsArray());
        limited = who with { Permissions = [AssistantPermissions.Catalog, AssistantPermissions.Retail] };
        Assert.Equal(403, (await Assert.ThrowsAsync<AssistantException>(() => service.DisplayAsync(limited, DisplayHistory(who, product, tiers: ["retail", "wholesale"]), default))).Status);
    }
}
