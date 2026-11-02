using GaoApp.Application.Services.Promotions;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Promotions;

public sealed class ProductPromotionPriceTests
{
    [Theory]
    [InlineData(37800, 15, 32100)]
    [InlineData(40000, 19.625, 32200)]
    [InlineData(40000, 19.75, 32100)]
    [InlineData(500, 0.1, 500)]
    [InlineData(10000, 100, 0)]
    [InlineData(10000, 200, 0)]
    public void Percentage_rounds_the_selling_unit_to_nearest_hundred(decimal retail, decimal percent, decimal expected)
        => Assert.Equal(expected, ProductPromotionPrice.SalePrice(retail, PromotionDiscountType.Percentage, percent));

    [Fact]
    public void Fixed_discount_remains_exact_and_never_increases_price()
    {
        Assert.Equal(32145m, ProductPromotionPrice.SalePrice(39000, PromotionDiscountType.FixedAmount, 6855));
        Assert.Equal(0m, ProductPromotionPrice.SalePrice(39000, PromotionDiscountType.FixedAmount, 40000));
        Assert.Equal(39000m, ProductPromotionPrice.SalePrice(39000, PromotionDiscountType.Percentage, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(1.5)]
    public async Task Both_POS_engine_entry_points_use_unit_price_before_multiplying(decimal quantity)
    {
        var promotion = new Promotion { Id=1, StoreId=1, Name="15%", Type=PromotionType.ProductDiscount,
            DiscountType=PromotionDiscountType.Percentage, DiscountValue=15, Items=[new() {ProductId=10}] };
        var repository = new PromotionTestRepository(); repository.Promotions.Add(promotion);
        var engine = new PromotionEngine(repository);
        foreach (var loaded in new[] { false, true })
        {
            var line = new OrderLine {StoreId=1, ProductId=10, VariantId=20, UnitPrice=37800, Quantity=quantity};
            var order = new Order {StoreId=1, Lines=[line]};
            if (loaded) await engine.ApplyOrderPromotionsAsync(order, [promotion], []);
            else await engine.ApplyLinePromotionAsync(order, line);
            Assert.Equal(32100m * quantity, line.UnitPrice * quantity - line.PromotionDiscount);
            Assert.Equal(37800m, line.OriginalUnitPrice);
        }
    }
}
