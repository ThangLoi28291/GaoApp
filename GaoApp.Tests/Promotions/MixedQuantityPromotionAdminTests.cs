using GaoApp.Application.DTOs.Promotions;
using GaoApp.Application.Services.Promotions;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Promotions;

public sealed class MixedQuantityPromotionAdminTests
{
    [Fact]
    public async Task Save_edit_update_and_duplicate_preserve_group_settings_and_normalize_members()
    {
        var repo = Repository();
        var service = new PromotionAdminService(repo);
        var request = Request();
        request.ComboBaseUnitId = 999;
        request.ComboRules[0].ProductUnitConversionId = 777;
        request.ComboRules[0].RequiredQuantity = 24;
        var created = await service.CreateAsync(1, request, null);
        Assert.True(created.IsSuccess);
        var entity = repo.Promotions.Single();
        Assert.Equal(1, entity.ComboBaseUnitId);
        Assert.All(entity.ComboRules, x =>
        {
            Assert.Null(x.ProductUnitConversionId);
            Assert.Equal(1, x.RequiredQuantity);
        });
        var edit = await service.GetForEditAsync(1, entity.Id);
        Assert.Equal(ComboPricingMode.MixedQuantity, edit.Value.ComboPricingMode);
        Assert.Equal(48, edit.Value.ComboQuantity);
        request.Id = entity.Id;
        request.ComboQuantity = 24;
        request.ComboFixedPrice = 200000;
        Assert.True((await service.UpdateAsync(1, request, null)).IsSuccess);
        var duplicate = await service.DuplicateAsync(1, entity.Id, null);
        Assert.True(duplicate.IsSuccess);
        var copy = repo.Promotions.Single(x => x.Id == duplicate.Value);
        Assert.Equal(24, copy.ComboQuantity);
        Assert.Equal(200000, copy.ComboFixedPrice);
        Assert.Equal(ComboPricingMode.MixedQuantity, copy.ComboPricingMode);
        Assert.False(copy.IsActive);
    }

    [Theory]
    [InlineData("zero-quantity")]
    [InlineData("negative-quantity")]
    [InlineData("different-base-unit")]
    [InlineData("duplicate-variant")]
    [InlineData("unavailable-variant")]
    [InlineData("wrong-product")]
    [InlineData("missing-variant")]
    [InlineData("unknown-mode")]
    public async Task Invalid_group_cannot_be_saved(string kind)
    {
        var repo = Repository();
        var request = Request();
        switch (kind)
        {
            case "zero-quantity": request.ComboQuantity = 0; break;
            case "negative-quantity": request.ComboQuantity = -48; break;
            case "different-base-unit": repo.Products[2].BaseUnitId = 2; break;
            case "duplicate-variant": request.ComboRules[1].VariantId = 1; break;
            case "unavailable-variant": repo.Products.Remove(2); break;
            case "wrong-product": request.ComboRules[1].ProductId = 2; break;
            case "missing-variant": request.ComboRules[1].VariantId = null; break;
            case "unknown-mode": request.ComboPricingMode = (ComboPricingMode)99; break;
        }
        Assert.False((await new PromotionAdminService(repo).CreateAsync(1, request, null)).IsSuccess);
        Assert.Empty(repo.Promotions);
    }

    [Fact]
    public async Task Legacy_combo_defaults_and_switching_back_remove_group_only_settings()
    {
        var repo = Repository();
        var service = new PromotionAdminService(repo);
        var request = Request();
        var created = await service.CreateAsync(1, request, null);
        request.Id = created.Value;
        request.ComboPricingMode = ComboPricingMode.RequiredItems;
        request.ComboRules[0].RequiredQuantity = 24;
        request.ComboRules[1].RequiredQuantity = 24;
        Assert.True((await service.UpdateAsync(1, request, null)).IsSuccess);
        var entity = repo.Promotions.Single();
        Assert.Null(entity.ComboQuantity);
        Assert.Null(entity.ComboBaseUnitId);
        Assert.All(entity.ComboRules.Where(x => !x.IsDeleted), x => Assert.Equal(24, x.RequiredQuantity));
        Assert.Equal(ComboPricingMode.RequiredItems, new SavePromotionRequest().ComboPricingMode);
    }

    private static PromotionTestRepository Repository()
    {
        var repository = new PromotionTestRepository();
        foreach (var variant in new[] { 1, 2 })
            repository.Products[variant] = new PromotionProductLookupDto
            {
                VariantId = variant, ProductId = 1, BaseUnitId = 1, BaseUnitName = "Hộp"
            };
        return repository;
    }

    private static SavePromotionRequest Request() => new()
    {
        Name = "TH ghép vị", Type = PromotionType.ComboFixedPrice,
        ComboPricingMode = ComboPricingMode.MixedQuantity, ComboQuantity = 48,
        ComboFixedPrice = 400000, StartAtUtc = DateTime.UtcNow.AddDays(-1),
        EndAtUtc = DateTime.UtcNow.AddDays(30),
        ComboRules = [new() { ProductId = 1, VariantId = 1 }, new() { ProductId = 1, VariantId = 2 }]
    };
}
