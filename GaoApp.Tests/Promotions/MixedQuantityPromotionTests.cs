using GaoApp.Application.Services.Orders;
using GaoApp.Application.Services.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using System.Reflection;

namespace GaoApp.Tests.Promotions;

public sealed class MixedQuantityPromotionTests
{
    [Theory]
    [InlineData(24, 24, 400000)]
    [InlineData(30, 18, 400000)]
    [InlineData(25, 25, 416667)]
    [InlineData(49, 1, 416667)]
    [InlineData(24, 25, 408333)]
    [InlineData(48, 48, 800000)]
    [InlineData(24, 23, 470000)]
    [InlineData(48, 0, 400000)]
    public async Task Threshold_prices_every_eligible_base_unit_including_quantity_over_a_pack(
        decimal first, decimal second, decimal expected)
    {
        var order = Sale(Line(1, first), Line(2, second));
        await Apply(order, Mixed());
        Assert.Equal(expected, order.GrandTotal);
        Assert.Equal(expected, order.Lines.Sum(x => x.LineTotal));
        Assert.Equal(0, order.ComboDiscountTotal);
        Assert.Equal(first + second, order.Lines.Sum(x => x.Quantity));
    }

    [Fact]
    public async Task Arbitrary_three_flavour_mix_reconciles_dong_rounding()
    {
        var promotion = Mixed();
        promotion.ComboNote = "Ghép vị từ 48 hộp";
        promotion.ComboRules.Add(Member(3));
        var order = Sale(Line(1, 1), Line(2, 1), Line(3, 46));
        await Apply(order, promotion);
        Assert.Equal(400000, order.GrandTotal);
        Assert.Equal(order.GrandTotal, order.Lines.Sum(x => x.LineTotal));
        Assert.Equal(80000, order.Lines.Sum(x => x.PromotionDiscount));
        Assert.All(order.Lines, line => Assert.Equal(promotion.ComboNote, line.ComboPromotionNote));
    }

    [Fact]
    public async Task Fractional_pack_unit_prices_reconcile_line_totals_and_order_total_to_the_group_price()
    {
        var order = Sale(Line(1, 25), Line(2, 25));
        foreach (var line in order.Lines) line.UnitPrice = 9166.666667m;
        await Apply(order, Mixed());
        Assert.Equal(416667, order.GrandTotal);
        Assert.Equal(order.GrandTotal, order.Lines.Sum(x => x.LineTotal));
    }

    [Fact]
    public async Task Boxes_and_packs_pool_using_snapshot_multiplier_even_if_base_quantity_is_stale()
    {
        var order = Sale(Line(1, 24), Line(2, 4, 6));
        order.Lines.Last().BaseQuantity = 999;
        await Apply(order, Mixed());
        Assert.Equal(400000, order.GrandTotal);
        Assert.Equal(4, order.Lines.Last().Quantity);
        Assert.Equal(6, order.Lines.Last().Multiplier);
    }

    [Fact]
    public async Task Repeated_recalculation_and_removal_restore_normal_price_without_mutating_unit_price()
    {
        var order = Sale(Line(1, 24), Line(2, 24));
        await Apply(order, Mixed());
        await Apply(order, Mixed());
        Assert.Equal(400000, order.GrandTotal);
        Assert.All(order.Lines, x => Assert.Equal(10000, x.UnitPrice));
        order.Lines.Last().Quantity = 23;
        await Apply(order, Mixed());
        Assert.Equal(470000, order.GrandTotal);
        Assert.All(order.Lines, x => Assert.Null(x.PromotionId));
        order.Lines.Last().Quantity = 24;
        await Apply(order, Mixed());
        order.Lines.Last().IsDeleted = true;
        await Apply(order, Mixed());
        Assert.Equal(240000, order.GrandTotal);
    }

    [Theory]
    [InlineData("different-unit")]
    [InlineData("different-store")]
    [InlineData("different-product")]
    [InlineData("deleted")]
    public async Task Ineligible_lines_do_not_help_reach_threshold(string kind)
    {
        var order = Sale(Line(1, 24), Line(2, 24));
        var second = order.Lines.Last();
        switch (kind)
        {
            case "different-unit": second.BaseUnitId = 2; break;
            case "different-store": second.StoreId = 2; break;
            case "different-product": second.ProductId = 9; break;
            case "deleted": second.IsDeleted = true; break;
        }
        await Apply(order, Mixed());
        Assert.All(order.Lines, x => Assert.Equal(0, x.PromotionDiscount));
    }

    [Fact]
    public async Task Gifts_are_not_counted_as_purchased_units()
    {
        var order = Sale(Line(1, 47));
        var giftPromotion = new Promotion
        {
            Id = 2, StoreId = 1, Type = PromotionType.BuyXGetY, Name = "Mua 47 tặng 1",
            BuyQuantity = 47, GetQuantity = 1, RequireGiftQuantityInCart = true,
            Items = [new PromotionItem { ProductId = 1, VariantId = 1 }]
        };
        await new PromotionEngine(new PromotionTestRepository()).ApplyOrderPromotionsAsync(
            order, [giftPromotion], [Mixed()]);
        Recalc(order);
        Assert.Equal(470000, order.GrandTotal);
        Assert.Contains(order.Lines, x => x.IsPromotionGift && x.Quantity == 1);
        Assert.All(order.Lines, x => Assert.Equal(0, x.PromotionDiscount));
    }

    [Fact]
    public async Task Repository_based_combo_entry_point_uses_the_same_group_pricing_and_clears_stale_discounts()
    {
        var repo = new PromotionTestRepository();
        repo.Promotions.Add(Mixed());
        var engine = new PromotionEngine(repo);
        var order = Sale(Line(1, 25), Line(2, 25));
        await engine.ApplyOrderComboPromotionAsync(order);
        Recalc(order);
        Assert.Equal(416667, order.GrandTotal);
        order.Lines.Last().Quantity = 22;
        await engine.ApplyOrderComboPromotionAsync(order);
        Recalc(order);
        Assert.Equal(470000, order.GrandTotal);
        Assert.All(order.Lines, x => Assert.Null(x.PromotionId));
    }

    [Fact]
    public async Task Promotion_is_scoped_to_store_and_customer_tier()
    {
        var order = Sale(Line(1, 24), Line(2, 24));
        var promotion = Mixed();
        promotion.StoreId = 2;
        await Apply(order, promotion);
        Assert.Equal(480000, order.GrandTotal);
        promotion.StoreId = 1;
        promotion.CustomerPriceTier = "WHOLESALE";
        await Apply(order, promotion);
        Assert.Equal(480000, order.GrandTotal);
        order.Customer = new Customer { PriceTier = "WHOLESALE" };
        await Apply(order, promotion);
        Assert.Equal(400000, order.GrandTotal);
    }

    [Fact]
    public async Task Multiple_disjoint_groups_apply_but_overlapping_groups_do_not_reuse_quantities()
    {
        var order = Sale(Line(1, 24), Line(2, 24), Line(3, 24), Line(4, 24));
        var first = Mixed();
        first.Priority = 5;
        var overlap = Mixed();
        overlap.Id = 2;
        overlap.ComboFixedPrice = 350000;
        var separate = Mixed();
        separate.Id = 3;
        separate.ComboRules = [Member(3), Member(4)];
        await Apply(order, first, overlap, separate);
        Assert.Equal(800000, order.GrandTotal);
        Assert.Equal(new int?[] { 1, 1, 3, 3 }, order.Lines.Select(x => x.PromotionId));
    }

    [Fact]
    public async Task Better_product_discount_wins_and_is_not_stacked_with_group_discount()
    {
        var order = Sale(Line(1, 24), Line(2, 24));
        var product = new Promotion
        {
            Id = 9, StoreId = 1, Name = "Giảm 20%", Type = PromotionType.ProductDiscount,
            DiscountType = PromotionDiscountType.Percentage, DiscountValue = 20,
            Items = [new PromotionItem { ProductId = 1 }]
        };
        var engine = new PromotionEngine(new PromotionTestRepository());
        await engine.ApplyOrderPromotionsAsync(order, [product], [Mixed()]);
        Recalc(order);
        Assert.Equal(384000, order.GrandTotal);
        Assert.All(order.Lines, x => Assert.Equal(9, x.PromotionId));
        product.DiscountValue = 10;
        await engine.ApplyOrderPromotionsAsync(order, [product], [Mixed()]);
        Recalc(order);
        Assert.Equal(400000, order.GrandTotal);
        Assert.All(order.Lines, x => Assert.Equal(1, x.PromotionId));
    }

    [Fact]
    public async Task Legacy_required_item_combo_remains_fixed_composition_and_cannot_reuse_mixed_lines()
    {
        var legacy = Mixed();
        legacy.Id = 2;
        legacy.ComboPricingMode = ComboPricingMode.RequiredItems;
        legacy.ComboRules.First().RequiredQuantity = 24;
        legacy.ComboRules.Last().RequiredQuantity = 24;
        legacy.ComboFixedPrice = 350000;
        var order = Sale(Line(1, 30), Line(2, 18));
        await Apply(order, legacy);
        Assert.Equal(480000, order.GrandTotal);
        order.Lines.First().Quantity = 24;
        order.Lines.Last().Quantity = 24;
        await Apply(order, legacy);
        Assert.Equal(350000, order.GrandTotal);
        var mixed = Mixed();
        mixed.Priority = 5;
        await Apply(order, mixed, legacy);
        Assert.Equal(400000, order.GrandTotal);
        Assert.Equal(0, order.ComboDiscountTotal);
        legacy.Priority = 6;
        await Apply(order, mixed, legacy);
        Assert.Equal(350000, order.GrandTotal);
    }

    [Fact]
    public async Task Configurable_pack_size_is_not_hardcoded_to_48_and_lower_prices_never_increase()
    {
        var promotion = Mixed();
        promotion.ComboQuantity = 24;
        promotion.ComboFixedPrice = 200000;
        var order = Sale(Line(1, 12), Line(2, 13));
        await Apply(order, promotion);
        Assert.Equal(208333, order.GrandTotal);
        foreach (var line in order.Lines) line.UnitPrice = 8000;
        await Apply(order, promotion);
        Assert.Equal(200000, order.GrandTotal);
    }

    internal static Promotion Mixed() => new()
    {
        Id = 1, StoreId = 1, Name = "TH ghép vị", Type = PromotionType.ComboFixedPrice,
        ComboPricingMode = ComboPricingMode.MixedQuantity, ComboQuantity = 48,
        ComboFixedPrice = 400000, ComboBaseUnitId = 1, ComboRules = [Member(1), Member(2)]
    };
    private static PromotionComboRule Member(int variant) => new() { ProductId = 1, VariantId = variant };
    private static OrderLine Line(int variant, decimal quantity, decimal multiplier = 1) => new()
    {
        Id = variant, StoreId = 1, ProductId = 1, VariantId = variant,
        BaseUnitId = 1, Quantity = quantity, Multiplier = multiplier,
        UnitPrice = 10000 * multiplier, BaseQuantity = quantity * multiplier
    };
    private static Order Sale(params OrderLine[] lines) => new() { StoreId = 1, Lines = lines.ToList() };
    private static async Task Apply(Order order, params Promotion[] promotions)
    {
        await new PromotionEngine(new PromotionTestRepository()).ApplyOrderPromotionsAsync(order, [], promotions);
        Recalc(order);
    }
    internal static void Recalc(Order order)
        => typeof(POSService).GetMethod("Recalc", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [order]);
}
