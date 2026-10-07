using System.Globalization;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Application.Services.Inventory;
using GaoApp.Domain.Entities;

namespace GaoApp.Tests.Inventory;

[Trait("Category", "DeliveryD05")]
public sealed class InventoryCostBudgetPolicyTests
{
    private static decimal Number(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("0.0002", "0.0001", "0.0001", "0.0001")]
    [InlineData("0.0002", "0.0001", "0.0002", "0.0001")]
    [InlineData("3", "1", "1", "0.3333")]
    [InlineData("3", "1", "2", "0.6667")]
    [InlineData("99999999999999.9998", "99999999999999.9998", "49999999999999.9999", "49999999999999.9999")]
    [InlineData("99999999999999.9999", "0.0001", "99999999999999.9999", "0.0001")]
    public void Cumulative_amount_uses_exact_integer_ratio_and_rounds_the_midpoint_once(
        string quantity, string amount, string consumed, string expected)
        => Assert.Equal(Number(expected), InventoryCostBudgetPolicy.Cumulative(
            Number(quantity), Number(amount), Number(consumed)));

    [Theory]
    [InlineData("0.0002", "0.0001", "0.0001,0.0001", "0.0001,0")]
    [InlineData("3", "1", "1,1,1", "0.3333,0.3334,0.3333")]
    [InlineData("1", "12.3457", "0.0001,0.0002,0.9997", "0.0012,0.0025,12.342")]
    public void Every_partition_conserves_the_frozen_budget_including_the_last_zero_residual(
        string quantity, string amount, string chunks, string expectedAmounts)
    {
        var layer = new InventoryCostLayer { CostBudgetPolicy = 1, OriginalQuantity = Number(quantity),
            RemainingQuantity = Number(quantity), BudgetAnchorQuantity = Number(quantity),
            BudgetAnchorAmount = Number(amount), RemainingCostAmount = Number(amount), UnitCost = 0.5m };
        var actual = chunks.Split(',').Select(chunk => InventoryCostBudgetPolicy.Debit(layer, Number(chunk))).ToArray();
        Assert.Equal(expectedAmounts.Split(',').Select(Number).ToArray(), actual);
        Assert.Equal(Number(amount), actual.Sum());
        Assert.Equal(0m, layer.RemainingQuantity);
        Assert.Equal(0m, layer.RemainingCostAmount);
        Assert.Equal(0.5m, layer.UnitCost); // Source provenance does not become a residual average.
    }

    [Theory]
    [InlineData("5", "10", "2", "-1")]
    [InlineData("5", "10", "-1", "2")]
    [InlineData("5", "10", "4", "2")]
    [InlineData("5", "10", "0", "0.00001")]
    [InlineData("5", "10", "0.00001", "1")]
    [InlineData("0", "10", "0", "0")]
    [InlineData("5", "-0.0001", "0", "1")]
    public void Invalid_interval_is_rejected_even_when_a_negative_slice_would_end_inside_the_anchor(
        string quantity, string amount, string start, string take)
        => Assert.Throws<BusinessRuleException>(() => InventoryCostBudgetPolicy.Slice(
            Number(quantity), Number(amount), Number(start), Number(take)));

    [Theory]
    [InlineData("quantity", "100000000000000")]
    [InlineData("quantity", "0.00001")]
    [InlineData("amount", "100000000000000")]
    [InlineData("amount", "0.00001")]
    [InlineData("cost", "1000000000000")]
    [InlineData("cost", "0.0000001")]
    [InlineData("cost", "-0.000001")]
    public void SQL_precision_boundaries_are_rejected_without_silent_rounding(string kind, string raw)
    {
        var number = Number(raw);
        Assert.Throws<BusinessRuleException>(() => kind switch
        {
            "quantity" => InventoryPostingPrecisionPolicy.Quantity(number),
            "amount" => InventoryPostingPrecisionPolicy.Amount(number),
            _ => InventoryPostingPrecisionPolicy.Cost(number)
        });
    }

    [Theory]
    [InlineData("policy")]
    [InlineData("missing-quantity")]
    [InlineData("missing-amount")]
    [InlineData("missing-remaining-amount")]
    [InlineData("wrong-residual")]
    [InlineData("overrun")]
    public void Invalid_or_unknown_layer_budget_fails_before_mutating_remaining_values(string fault)
    {
        var layer = new InventoryCostLayer { CostBudgetPolicy = 1, OriginalQuantity = 2, RemainingQuantity = 1,
            BudgetAnchorQuantity = 2, BudgetAnchorAmount = 20, RemainingCostAmount = 10, UnitCost = 10 };
        var take = 0.5m;
        switch (fault)
        {
            case "policy": layer.CostBudgetPolicy = 0; break;
            case "missing-quantity": layer.BudgetAnchorQuantity = null; break;
            case "missing-amount": layer.BudgetAnchorAmount = null; break;
            case "missing-remaining-amount": layer.RemainingCostAmount = null; break;
            case "wrong-residual": layer.RemainingCostAmount = 9.9999m; break;
            case "overrun": take = 1.0001m; break;
        }
        var before = (layer.CostBudgetPolicy, layer.BudgetAnchorQuantity, layer.BudgetAnchorAmount,
            layer.RemainingQuantity, layer.RemainingCostAmount, layer.UnitCost);
        var error = Assert.Throws<BusinessRuleException>(() => InventoryCostBudgetPolicy.Debit(layer, take));
        Assert.Contains("COST_NOT_READY", error.Message, StringComparison.Ordinal);
        Assert.Equal(before, (layer.CostBudgetPolicy, layer.BudgetAnchorQuantity, layer.BudgetAnchorAmount,
            layer.RemainingQuantity, layer.RemainingCostAmount, layer.UnitCost));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    public void Genuine_zero_budget_and_positive_unit_zero_residual_are_distinct_provenance(string unit)
    {
        var layer = new InventoryCostLayer { CostBudgetPolicy = 1, OriginalQuantity = 0.0001m,
            RemainingQuantity = 0.0001m, BudgetAnchorQuantity = 0.0001m, BudgetAnchorAmount = 0,
            RemainingCostAmount = 0, UnitCost = Number(unit) };
        Assert.Equal(0m, InventoryCostBudgetPolicy.Debit(layer, 0.0001m));
        Assert.Equal(Number(unit), layer.UnitCost);
        Assert.Equal(0m, layer.RemainingQuantity);
        Assert.Equal(0m, layer.RemainingCostAmount);
    }
}
