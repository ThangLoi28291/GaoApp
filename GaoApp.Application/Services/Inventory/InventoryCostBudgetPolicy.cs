using System.Numerics;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Inventory;

public static class InventoryCostBudgetPolicy
{
    private static BigInteger Scaled(decimal value) => new(value * 10000m);
    public static decimal Cumulative(decimal anchorQuantity, decimal anchorAmount, decimal consumedQuantity)
    {
        InventoryPostingPrecisionPolicy.Quantity(anchorQuantity);
        InventoryPostingPrecisionPolicy.Quantity(consumedQuantity);
        InventoryPostingPrecisionPolicy.Amount(anchorAmount);
        if (anchorQuantity <= 0 || anchorAmount < 0 || consumedQuantity < 0 || consumedQuantity > anchorQuantity)
            throw new BusinessRuleException("COST_BUDGET_INVALID: khoảng giá vốn không hợp lệ.");
        var denominator = Scaled(anchorQuantity);
        var quotient = BigInteger.DivRem(Scaled(anchorAmount) * Scaled(consumedQuantity), denominator, out var remainder);
        if (remainder * 2 >= denominator) quotient++;
        return (decimal)quotient / 10000m;
    }
    public static decimal Slice(decimal anchorQuantity, decimal anchorAmount, decimal start, decimal quantity)
    {
        InventoryPostingPrecisionPolicy.Quantity(start); InventoryPostingPrecisionPolicy.Quantity(quantity);
        if (start < 0 || quantity < 0 || checked(start + quantity) > anchorQuantity)
            throw new BusinessRuleException("COST_BUDGET_INVALID: khoảng giá vốn không hợp lệ.");
        return Cumulative(anchorQuantity, anchorAmount, start + quantity) - Cumulative(anchorQuantity, anchorAmount, start);
    }

    public static decimal Debit(InventoryCostLayer layer, decimal quantity)
    {
        if (layer.CostBudgetPolicy != 1 || layer.BudgetAnchorQuantity is not decimal q ||
            layer.BudgetAnchorAmount is not decimal amount || layer.RemainingCostAmount is not decimal remaining ||
            layer.RemainingQuantity < quantity || quantity <= 0 || remaining != amount - Cumulative(q, amount, q - layer.RemainingQuantity))
            throw new BusinessRuleException("COST_NOT_READY: ngân sách FIFO không khớp nguồn.");
        var debit = Slice(q, amount, q - layer.RemainingQuantity, quantity);
        layer.RemainingQuantity = InventoryPostingPrecisionPolicy.Quantity(layer.RemainingQuantity - quantity);
        layer.RemainingCostAmount = InventoryPostingPrecisionPolicy.Amount(remaining - debit);
        return debit;
    }
}
