using GaoApp.Application.Interfaces.Repositories.Rewards;
using GaoApp.Application.Services.Rewards;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Tests.Rewards;

public sealed class OrderRewardCalculatorTests
{
    [Theory]
    [InlineData(1, 6000, 1, 6000)]
    [InlineData(3, 6000, 1, 18000)]
    [InlineData(4, 6000, 1, 24000)]
    [InlineData(4, 5000, 1, 0)]
    [InlineData(1, 20000, 4, 0)]
    [InlineData(1, 5500, 1, 0)]
    [InlineData(1, 7000, 1, 0)]
    public async Task Only_actual_base_unit_at_unchanged_retail_price_earns(
        decimal quantity, decimal price, decimal multiplier, decimal expected)
    {
        var order = Sale(quantity, price, multiplier);
        // A legacy quantity threshold must not exclude units still sold at the full retail price.
        order.Lines.Single().Variant!.Product.RewardBulkExcludeQuantity = 4;
        var result = await Calculator(order).CalculateAsync(order.Id);
        Assert.Equal(expected, result.RewardableAmount);
        Assert.Equal(expected > 0, result.Lines.Single().IsRewardable);
    }

    [Fact]
    public async Task Fractional_base_quantity_uses_the_same_dong_rounding_as_pos()
    {
        var order = Sale(1.2346m);
        order.Lines.Single().LineTotal = 7408;
        Assert.Equal(7408, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
    }

    [Fact]
    public async Task Factor_one_alone_does_not_make_another_selling_unit_eligible()
    {
        var order = Sale();
        order.Lines.Single().SellingUnitId = 99;
        Assert.Equal(0, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
    }

    [Theory]
    [InlineData("line")]
    [InlineData("promotion")]
    [InlineData("combo")]
    [InlineData("gift")]
    [InlineData("total")]
    public async Task Discounted_or_gift_line_never_earns(string kind)
    {
        var order = Sale();
        var line = order.Lines.Single();
        switch (kind)
        {
            case "line": line.LineDiscount = 100; break;
            case "promotion": line.PromotionDiscount = 100; break;
            case "combo": line.ComboAllocatedDiscount = 100; break;
            case "gift": line.IsPromotionGift = true; break;
            case "total": line.LineTotal = 5900; break;
        }
        Assert.Equal(0, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excluded_category_or_ancestor_wins_over_product_opt_in(bool ancestor)
    {
        var order = Sale();
        var product = order.Lines.Single().Variant!.Product;
        product.IsRewardEligibleOverride = true;
        if (ancestor)
        {
            product.Category.ParentId = 2;
            product.Category.Parent = new Category { Id = 2, IsRewardEligible = false };
        }
        else product.Category.IsRewardEligible = false;
        Assert.Equal(0, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
    }

    [Fact]
    public async Task Allowed_hierarchy_earns_and_a_broken_or_cyclic_hierarchy_does_not()
    {
        var order = Sale();
        var category = order.Lines.Single().Variant!.Product.Category;
        category.ParentId = 2;
        category.Parent = new Category { Id = 2, IsRewardEligible = true };
        var calculator = Calculator(order);
        Assert.Equal(6000, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
        category.Parent.ParentId = category.Id;
        category.Parent.Parent = category;
        Assert.Equal(0, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
        category.Parent = null;
        Assert.Equal(0, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
    }

    [Fact]
    public async Task Base_retail_snapshot_survives_catalog_repricing_without_using_promotion_original_price()
    {
        var order = Sale();
        var line = order.Lines.Single();
        line.RewardBaseUnitPrice = 6000;
        line.Variant!.UnitConversions.Single().Price = 9000;
        line.OriginalUnitPrice = 9000;
        Assert.Equal(6000, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
        line.UnitPrice = 5000;
        line.LineTotal = 5000;
        line.OriginalUnitPrice = 5000;
        Assert.Equal(0, (await Calculator(order).CalculateAsync(order.Id)).RewardableAmount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6000)]
    public async Task Return_uses_recorded_earning_even_after_price_and_category_changes(decimal earned)
    {
        var order = Sale();
        var line = order.Lines.Single();
        line.RewardableAmountSnapshot = earned;
        line.Variant!.Product.Category.IsRewardEligible = false;
        line.Variant.UnitConversions.Single().Price = 9000;
        Assert.Equal(earned, (await Calculator(order).CalculateForReturnAsync(order.Id)).RewardableAmount);
    }

    [Fact]
    public async Task Legacy_returns_keep_the_original_quantity_rule_but_new_sales_use_actual_price()
    {
        var order = Sale(4);
        order.Lines.Single().Variant!.Product.RewardBulkExcludeQuantity = 4;
        var calculator = Calculator(order);
        Assert.Equal(24000, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
        Assert.Equal(0, (await calculator.CalculateForReturnAsync(order.Id)).RewardableAmount);
    }

    [Fact]
    public async Task Deleted_lines_anonymous_customers_and_unfinished_orders_do_not_earn()
    {
        var order = Sale();
        var calculator = Calculator(order);
        order.Lines.Single().IsDeleted = true;
        Assert.Equal(0, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
        order.Lines.Single().IsDeleted = false;
        order.CustomerId = null;
        Assert.Equal(0, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
        order.CustomerId = 1;
        order.Status = OrderStatus.Draft;
        Assert.Equal(0, (await calculator.CalculateAsync(order.Id)).RewardableAmount);
    }

    private static Order Sale(decimal quantity = 1, decimal price = 6000, decimal multiplier = 1)
    {
        var product = new Product { Id = 1, BaseUnitId = 1, BasePrice = 6000, CategoryId = 1,
            Category = new Category { Id = 1, Name = "Sữa", IsRewardEligible = true } };
        var variant = new ProductVariant { Id = 1, Product = product, Price = 6000 };
        variant.UnitConversions.Add(new ProductUnitConversion {
            Id = 1, UnitId = 1, IsActive = true, IsBaseUnit = true, Factor = 1, Price = 6000 });
        return new Order { Id = 1, CustomerId = 1, Status = OrderStatus.Completed, Lines = new List<OrderLine> {
            new() { Id = 1, ProductId = 1, VariantId = 1, Variant = variant, Quantity = quantity,
                UnitPrice = price, LineTotal = quantity * price, Multiplier = multiplier,
                SellingUnitId = multiplier == 1 ? 1 : 2, BaseUnitId = 1 } } };
    }

    private static OrderRewardCalculator Calculator(Order order) => new(new Orders(order));
    private sealed class Orders(Order order) : IRewardOrderRepository
    {
        public Task<Order?> GetOrderForRewardCalculationAsync(int orderId, CancellationToken ct = default)
            => Task.FromResult<Order?>(order.Id == orderId ? order : null);
    }
}
