using GaoApp.Application.Storefront;
using GaoApp.Domain.Delivery;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Storefront;

[Collection("StorefrontWeb04"), Trait("Category", "StorefrontWeb04")]
public sealed class Web04PromotionTests(Web01Fixture fixture)
{
    [Theory]
    [InlineData("0.0001")][InlineData("1")][InlineData("1.5")][InlineData("2")]
    public async Task Product_discount_quote_matches_actual_POS_engine_unit_rounding_and_approved_line_allocation(string quantity)
    {
        var c = await fixture.Web04Async(); await SetBasePrice(c, 37800); await Promotion(c, new() { Name = "WEB04 product15", Type = PromotionType.ProductDiscount,
            DiscountType = PromotionDiscountType.Percentage, DiscountValue = 15, Items = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId }] });
        var request = c.Request(decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture)); await Parity(c, request);
    }
    [Theory]
    [InlineData(24, 24)][InlineData(25, 25)][InlineData(24, 23)]
    public async Task Mixed_quantity_discount_matches_POS_for_threshold_fractional_allocation_and_below_threshold(decimal first, decimal second)
    {
        var c = await fixture.Web04Async(); var other = await SecondVariant(c); await Promotion(c, Mixed(c, other.Variant));
        var request = c.Request(first); request.Items.Add(new() { ProductId = c.Seed.ProductId, VariantId = other.Variant, UnitId = c.Seed.UnitId, Quantity = second }); await Parity(c, request);
    }
    [Fact]
    public async Task Mixed_base_and_pack_quantities_pool_as_POS_without_repricing_stored_pack_unit()
    {
        var c = await fixture.Web04Async(); var other = await SecondVariant(c); await Promotion(c, Mixed(c, other.Variant));
        var request = c.Request(24); request.Items.Add(new() { ProductId = c.Seed.ProductId, VariantId = other.Variant, UnitId = other.PackUnit, Quantity = 4 });
        var quote = await Parity(c, request); Assert.Equal(400000, quote.GoodsAmount); Assert.Equal(60000, quote.Lines.Single(x => x.VariantId == other.Variant).UnitPrice);
    }
    [Fact]
    public async Task Hidden_optional_mixed_pool_member_not_bought_does_not_disable_valid_A_only_promotion()
    {
        var c = await fixture.Web04Async(); var other = await SecondVariant(c); await Promotion(c, Mixed(c, other.Variant));
        await using (var db = c.Context()) await db.StorefrontVariants.Where(x => x.VariantId == other.Variant).ExecuteUpdateAsync(x => x.SetProperty(v => v.IsPublished, false));
        var quote = await Parity(c, c.Request(48)); Assert.Equal(400000, quote.GoodsAmount);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Required_item_combo_and_missing_required_member_follow_actual_POS_activation(bool complete)
    {
        var c = await fixture.Web04Async(); var other = await SecondVariant(c); await Promotion(c, new() { Name = "WEB04 required combo", Type = PromotionType.ComboFixedPrice,
            ComboPricingMode = ComboPricingMode.RequiredItems, ComboFixedPrice = 15000,
            ComboRules = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId, RequiredQuantity = 1 }, new() { ProductId = c.Seed.ProductId, VariantId = other.Variant, RequiredQuantity = 1 }] });
        var request = c.Request(complete ? 1 : 2); if (complete) request.Items.Add(new() { ProductId = c.Seed.ProductId, VariantId = other.Variant, UnitId = c.Seed.UnitId, Quantity = 1 });
        var quote = await Parity(c, request); Assert.Equal(complete ? 15000 : 20000, quote.GoodsAmount);
    }
    [Fact]
    public async Task Better_product_discount_is_not_stacked_with_mixed_combo()
    {
        var c = await fixture.Web04Async(); var other = await SecondVariant(c); await Promotion(c, Mixed(c, other.Variant));
        await Promotion(c, new() { Name = "WEB04 better20", Priority = 10, Type = PromotionType.ProductDiscount,
            DiscountType = PromotionDiscountType.Percentage, DiscountValue = 20, Items = [new() { ProductId = c.Seed.ProductId }] });
        var request = c.Request(24); request.Items.Add(new() { ProductId = c.Seed.ProductId, VariantId = other.Variant, UnitId = c.Seed.UnitId, Quantity = 24 });
        var quote = await Parity(c, request); Assert.Equal(384000, quote.GoodsAmount);
    }
    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task Actual_buy_two_get_one_gift_lines_source_identity_quantity_kg_and_holds_are_preserved(bool pack)
    {
        var c = await fixture.Web04Async(); var promotion = await Promotion(c, new() { Name = "WEB04 buy2gift1", Type = PromotionType.BuyXGetY,
            BuyQuantity = 2, GetQuantity = 1, RequireGiftQuantityInCart = true, Items = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId }] });
        if (pack) await c.SettingsAsync(kg: true);
        var quote = await Parity(c, c.Request(3, pack)); var gift = Assert.Single(quote.Lines, x => x.IsGift); Assert.Equal(1, gift.Quantity); Assert.Equal(0, gift.UnitPrice); Assert.Equal(0, gift.NetAmount);
        Assert.Equal(2, quote.Lines.Count); Assert.Equal(promotion.Name, gift.PromotionName); Assert.Equal(2, quote.Lines.Select(x => x.Index).Distinct().Count());
        var placed = await c.PlaceAsync(Web04Case.PlaceRequest(quote)); Assert.Equal(StorefrontPlacementStatus.Placed, placed.Status);
        await using var db = c.Context(); var lines = await db.Set<GaoApp.Domain.Storefront.StorefrontOrderLine>().ToListAsync(); Assert.Equal(2, lines.Count); Assert.Single(lines, x => x.IsGift);
        Assert.Equal(3, quote.Lines.Single(x => !x.IsGift).Quantity);
        Assert.Equal(pack ? 48 : 4, await db.InventoryReservations.SumAsync(x => x.ReservedQty)); Assert.Equal(pack ? 48 : 4, (await db.InventoryBalances.SingleAsync()).ReservedQty);
        Assert.Equal(100, (await db.InventoryBalances.SingleAsync()).OnHandQty); Assert.Empty(await db.InventoryTransactions.ToListAsync()); Assert.Empty(await db.Orders.ToListAsync());
        if (pack) { Assert.Equal(5.6m, quote.TotalWeightKg); Assert.Equal(70000, quote.ShippingCharge); }
    }
    [Fact]
    public async Task Matching_guest_phone_never_grants_customer_wholesale_price_or_wholesale_only_promotion()
    {
        var c = await fixture.Web04Async(); await using (var db = c.Context())
        {
            db.Customers.Add(new() { StoreId = c.StoreId, Name = "Synthetic wholesale same phone", Phone = c.Request().RecipientPhone, PriceTier = "WHOLESALE", Code = "W04-WHOLESALE" }); await db.SaveChangesAsync();
        }
        await Promotion(c, new() { Name = "Wholesale only", CustomerPriceTier = "WHOLESALE", Type = PromotionType.ProductDiscount,
            DiscountType = PromotionDiscountType.Percentage, DiscountValue = 80, Items = [new() { ProductId = c.Seed.ProductId }] });
        var quote = await Parity(c, c.Request()); Assert.Equal(25000, quote.GoodsAmount); Assert.Equal(25000, Assert.Single(quote.Lines).UnitPrice);
    }
    [Theory]
    [InlineData(4000, 1, 1)][InlineData(25000, 5, -1)]
    public async Task Fractional_normal_POS_aggregate_rounding_is_reconciled_without_changing_source_prices(decimal price, decimal total, decimal adjustment)
    {
        var c = await fixture.Web04Async(); await SetBasePrice(c, price); var second = await c.Seed.AddProductAsync("WEB04 fractional second", price: price);
        var request = c.Request(0.0001m); request.Items.Add(new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 0.0001m });
        var quote = await Parity(c, request); Assert.Equal(total, quote.GoodsAmount); Assert.Equal(adjustment, quote.Lines.Sum(x => x.RoundingAdjustment));
        Assert.All(quote.Lines, x => Assert.Equal(price, x.UnitPrice)); Assert.Equal(total, quote.Lines.Sum(x => x.NetAmount));
    }
    [Fact]
    public async Task Large_exact_fractional_POS_rounding_and_BigInteger_allocation_remain_within_money_bound()
    {
        var c = await fixture.Web04Async(); await SetBasePrice(c, 4503599627370496.50m); var second = await c.Seed.AddProductAsync("WEB04 exact large second", price: 4503599627370496.50m);
        var request = c.Request(); request.Items.Add(new() { ProductId = second.ProductId, VariantId = second.VariantId, UnitId = second.UnitId, Quantity = 1 });
        var quote = await Parity(c, request); Assert.Equal(9007199254740993m, quote.GoodsAmount); Assert.Equal(-1, quote.Lines.Sum(x => x.RoundingAdjustment));
    }

    internal static async Task<StorefrontQuoteDto> Parity(Web04Case c, StorefrontQuoteRequest request)
    {
        Web04PosResult expected; await using (var db = c.Context()) expected = await Web04PosOracle.PriceAsync(db, request.Items);
        var quote = await c.QuoteAsync(request); Assert.Equal(expected.Order.GrandTotal, quote.GoodsAmount); Assert.Equal(expected.Approved.Sum(x => x.Net), quote.GoodsAmount);
        var sources = expected.Order.Lines.Where(x => !x.IsDeleted).ToArray(); Assert.Equal(sources.Length, quote.Lines.Count);
        foreach (var line in quote.Lines)
        {
            var source = Assert.Single(sources, x => x.ProductId == line.ProductId && x.VariantId == line.VariantId && x.SellingUnitId == line.UnitId && x.IsPromotionGift == line.IsGift);
            var approved = Assert.Single(expected.Approved, x => x.LineId == source.Id);
            Assert.Equal(source.Quantity, line.Quantity); Assert.Equal(source.Multiplier, line.BaseMultiplier); Assert.Equal(source.UnitPrice, line.UnitPrice);
            Assert.Equal(approved.Net, line.NetAmount); Assert.Equal(approved.LineDiscount + approved.AllocatedOrderDiscount, line.Discount);
            Assert.Equal(approved.Gross - DeliveryValues.PricedAmount(source.Quantity, source.UnitPrice), line.RoundingAdjustment);
            Assert.Equal(DeliveryValues.PricedAmount(line.Quantity, line.UnitPrice) + line.RoundingAdjustment - line.Discount, line.NetAmount);
        }
        return quote;
    }
    internal static async Task SetBasePrice(Web04Case c, decimal price)
    {
        await using var db = c.Context(); await db.ProductUnitConversions.Where(x => x.Id == c.Seed.ConversionId).ExecuteUpdateAsync(x => x.SetProperty(p => p.Price, price));
    }
    internal static async Task<Promotion> Promotion(Web04Case c, Promotion value)
    {
        value.StoreId = c.StoreId; value.IsActive = true; value.StartAtUtc = DateTime.UtcNow.AddDays(-1); value.EndAtUtc = DateTime.UtcNow.AddDays(1);
        foreach (var item in value.Items) item.StoreId = c.StoreId; foreach (var item in value.ComboRules) item.StoreId = c.StoreId;
        await using var db = c.Context(); db.Promotions.Add(value); await db.SaveChangesAsync(); return value;
    }
    private static Promotion Mixed(Web04Case c, int second) => new() { Name = "WEB04 mixed48", Type = PromotionType.ComboFixedPrice,
        ComboPricingMode = ComboPricingMode.MixedQuantity, ComboQuantity = 48, ComboFixedPrice = 400000, ComboBaseUnitId = c.Seed.UnitId,
        ComboRules = [new() { ProductId = c.Seed.ProductId, VariantId = c.Seed.VariantId }, new() { ProductId = c.Seed.ProductId, VariantId = second }] };
    private static async Task<(int Variant, int PackUnit)> SecondVariant(Web04Case c)
    {
        await SetBasePrice(c, 10000); await using var db = c.Context();
        var variant = new ProductVariant { StoreId = c.StoreId, ProductId = c.Seed.ProductId, Sku = "W04-SECOND-" + Guid.NewGuid().ToString("N"), Price = 10000, IsActive = true };
        var unit = new Unit { StoreId = c.StoreId, Code = "W04PACK6", Name = "Lốc6 fixture", IsActive = true }; db.AddRange(variant, unit); await db.SaveChangesAsync();
        db.ProductUnitConversions.Add(new() { StoreId = c.StoreId, ProductVariantId = variant.Id, UnitId = c.Seed.UnitId, Factor = 1, Price = 10000, IsBaseUnit = true });
        var pack = new ProductUnitConversion { StoreId = c.StoreId, ProductVariantId = variant.Id, UnitId = unit.Id, Factor = 6, Price = 60000 }; db.Add(pack); await db.SaveChangesAsync();
        var request = (await c.Seed.Products(db).GetAsync(c.Seed.ProductId)).Product; var selected = request.Variants.Single(x => x.VariantId == variant.Id);
        selected.IsPublished = selected.BaseUnitIsPublished = true; selected.BaseUnitWeightKg = 0.125m; selected.Units.Single(x => x.ProductUnitConversionId == pack.Id).IsPublished = true;
        selected.Units.Single(x => x.ProductUnitConversionId == pack.Id).WeightKg = 0.75m; await c.Seed.Products(db).SaveAsync(c.Seed.ProductId, request);
        return (variant.Id, unit.Id);
    }
}
