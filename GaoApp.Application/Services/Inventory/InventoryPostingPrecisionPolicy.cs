using GaoApp.Application.Common;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Inventory;

public static class InventoryPostingPrecisionPolicy
{
    public const decimal QuantityLimit = 100000000000000m;
    public const decimal CostLimit = 1000000000000m;
    public static decimal Quantity(decimal value)
    {
        if (value <= -QuantityLimit || value >= QuantityLimit || decimal.Round(value, 4) != value)
            throw new BusinessRuleException("INVENTORY_PRECISION: số lượng vượt giới hạn hoặc quá 4 số lẻ.");
        return value;
    }
    public static decimal Cost(decimal value)
    {
        if (value < 0 || value >= CostLimit || decimal.Round(value, 6) != value)
            throw new BusinessRuleException("COST_NOT_READY: giá vốn nguồn không hợp lệ.");
        return value;
    }
    public static decimal Amount(decimal value)
    {
        if (value <= -QuantityLimit || value >= QuantityLimit || decimal.Round(value, 4) != value)
            throw new BusinessRuleException("INVENTORY_PRECISION: giá trị tồn vượt giới hạn.");
        return value;
    }
    public static decimal EffectiveDelta(decimal raw, bool delivery)
    {
        var value = delivery ? Quantity(raw) : Quantity(decimal.Round(raw, 3, MidpointRounding.AwayFromZero));
        if (raw != 0 && value == 0) throw new BusinessRuleException("BELOW_LEGACY_STEP: số lượng nhỏ hơn bước kho legacy.");
        return value;
    }
}
