using FluentAssertions;
using GaoApp.Application.Services.Purchases;

namespace GaoApp.Tests.Purchases;

public sealed class PurchasePricingPolicyTests
{
    [Fact]
    public void Vat_off_should_force_zero_tax_and_equal_prices()
    {
        var result = PurchasePricingPolicy.CalculateLine(3m, 11_000m, false, 10m);
        result.UnitPriceBeforeVat.Should().Be(11_000m);
        result.UnitPriceAfterVat.Should().Be(11_000m);
        result.TaxRate.Should().Be(0m);
        result.VatAmount.Should().Be(0m);
        result.LineTotalBeforeVat.Should().Be(result.LineTotalAfterVat).And.Be(33_000m);
    }

    [Theory]
    [InlineData(5, 10500, 10000, 500)]
    [InlineData(8, 10800, 10000, 800)]
    [InlineData(10, 11000, 10000, 1000)]
    public void Vat_on_should_derive_before_vat_from_after_vat(
        decimal rate, decimal after, decimal expectedBefore, decimal expectedVat)
    {
        var result = PurchasePricingPolicy.CalculateLine(1m, after, true, rate);
        result.UnitPriceBeforeVat.Should().Be(expectedBefore);
        result.VatAmount.Should().Be(expectedVat);
        (result.LineTotalBeforeVat + result.VatAmount).Should().Be(result.LineTotalAfterVat);
    }

    [Fact]
    public void Vat_rounding_should_keep_line_identity()
    {
        var result = PurchasePricingPolicy.CalculateLine(3m, 10_001m, true, 8m);
        (result.LineTotalBeforeVat + result.VatAmount).Should().Be(result.LineTotalAfterVat);
        result.LineTotalAfterVat.Should().Be(30_003m);
    }

    [Fact]
    public void Automatic_freight_should_allocate_by_after_vat_amount_and_put_rounding_on_last_line()
    {
        var result = PurchasePricingPolicy.AllocateFreight(10_001m,
            new[] { (1, 100_000m), (2, 200_000m), (3, 300_000m) });
        result.Values.Sum().Should().Be(10_001m);
        result[1].Should().Be(1_666.83m);
        result[2].Should().Be(3_333.67m);
        result[3].Should().Be(5_000.50m);
    }

    [Fact]
    public void Freight_balance_should_reject_under_allocation()
    {
        var action = () => PurchasePricingPolicy.EnsureFreightBalanced(100m, new[] { 40m, 59.99m });
        action.Should().Throw<InvalidOperationException>().WithMessage("*còn thiếu*");
    }

    [Fact]
    public void Freight_balance_should_reject_over_allocation()
    {
        var action = () => PurchasePricingPolicy.EnsureFreightBalanced(100m, new[] { 40m, 60.01m });
        action.Should().Throw<InvalidOperationException>().WithMessage("*đang vượt*");
    }

    [Fact]
    public void Freight_balance_should_reject_negative_line_even_when_sum_is_exact()
    {
        var action = () => PurchasePricingPolicy.EnsureFreightBalanced(20m, new[] { -10m, 30m });
        action.Should().Throw<InvalidOperationException>().WithMessage("*không được âm*");
    }

    [Fact]
    public void Freight_balance_should_accept_exact_allocations()
    {
        var action = () => PurchasePricingPolicy.EnsureFreightBalanced(100m, new[] { 33.33m, 66.67m });
        action.Should().NotThrow();
    }

    [Theory]
    [InlineData(false, false, 8333.333333)]
    [InlineData(true, false, 9166.666667)]
    [InlineData(false, true, 9166.666667)]
    [InlineData(true, true, 10000)]
    public void Base_unit_cost_should_apply_only_selected_capitalization_components(
        bool includeVat,
        bool capitalizeFreight,
        decimal expected)
    {
        var cost = PurchasePricingPolicy.CalculateBaseUnitCost(
            merchandiseAmountBeforeVat: 200_000m,
            vatAmount: 20_000m,
            freightAllocation: 20_000m,
            baseQuantity: 24m,
            includeVatInInventoryCost: includeVat,
            capitalizeFreightInInventoryCost: capitalizeFreight);

        cost.Should().Be(expected);
    }

    [Fact]
    public void Base_unit_cost_should_round_midpoint_away_from_zero_at_six_decimals()
        => PurchasePricingPolicy.CalculateBaseUnitCost(1m, 0m, 0m, 128m, false, false)
            .Should().Be(0.007813m);

    [Fact]
    public void Base_unit_cost_should_reject_non_positive_base_quantity()
    {
        var action = () => PurchasePricingPolicy.CalculateBaseUnitCost(1m, 0m, 0m, 0m, false, false);
        action.Should().Throw<InvalidOperationException>().WithMessage("*lớn hơn 0*");
    }

    [Fact]
    public void Base_unit_cost_should_reject_negative_freight_component()
    {
        var action = () => PurchasePricingPolicy.CalculateBaseUnitCost(100m, 0m, -10m, 10m, false, true);
        action.Should().Throw<InvalidOperationException>().WithMessage("*không được âm*");
    }

    [Fact]
    public void Automatic_freight_should_reject_positive_freight_when_goods_total_is_zero()
    {
        var action = () => PurchasePricingPolicy.AllocateFreight(100m, new[] { (1, 0m) });
        action.Should().Throw<InvalidOperationException>().WithMessage("*tổng tiền hàng bằng 0*");
    }

    [Fact]
    public void Quantity_rounding_should_use_three_decimals_away_from_zero()
        => PurchasePricingPolicy.RoundQuantity(1.2345m).Should().Be(1.235m);
}
