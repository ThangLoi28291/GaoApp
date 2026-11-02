namespace GaoApp.Domain.Storefront;

public static class StorefrontShippingPolicy
{
    public static StorefrontShippingQuote Quote(int storeId, decimal goodsAmount,
        IReadOnlyList<StorefrontShippingLine> lines, StorefrontShippingRule rule)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(rule);
        StorefrontValues.Require(storeId > 0 && rule.StoreId == storeId && rule.RuleId > 0 && rule.Revision > 0,
            "SHIPPING_SCOPE_INVALID", "Bảng phí không thuộc phạm vi cửa hàng.");
        StorefrontValues.Money(goodsAmount);
        StorefrontValues.Money(rule.BaseFee);
        StorefrontValues.Money(rule.AdditionalFee);
        StorefrontValues.Require(goodsAmount > 0 && lines.Count > 0, "CART_INVALID", "Đơn phải có hàng và giá trị dương.");
        if (rule.FreeFromGoodsAmount.HasValue)
        {
            StorefrontValues.Money(rule.FreeFromGoodsAmount.Value);
            StorefrontValues.Require(rule.FreeFromGoodsAmount > 0, "FREE_THRESHOLD_INVALID", "Ngưỡng miễn phí phải dương.");
        }
        if (rule.ByWeight)
            StorefrontValues.Require(rule.FirstWeightKg > 0 && rule.AdditionalStepKg > 0 &&
                rule.FirstWeightKg < StorefrontValues.QuantityLimit && rule.AdditionalStepKg < StorefrontValues.QuantityLimit,
                "WEIGHT_RULE_INVALID", "Mức kg đầu và bước tăng phải dương.");

        decimal totalWeight = 0;
        var weightComplete = true;
        foreach (var line in lines)
        {
            ArgumentNullException.ThrowIfNull(line);
            StorefrontValues.Quantity(line.Quantity, positive: true);
            if (!line.WeightKgPerSellingUnit.HasValue)
            {
                weightComplete = false;
                StorefrontValues.Require(!rule.ByWeight, "PRODUCT_WEIGHT_REQUIRED", "Thiếu kg của đơn vị bán.");
                continue;
            }
            StorefrontValues.Require(line.WeightKgPerSellingUnit > 0 && line.WeightKgPerSellingUnit < StorefrontValues.QuantityLimit,
                "PRODUCT_WEIGHT_INVALID", "Kg của đơn vị bán phải dương.");
            try { totalWeight = checked(totalWeight + line.Quantity * line.WeightKgPerSellingUnit.Value); }
            catch (OverflowException) { throw new StorefrontRuleException("WEIGHT_OVERFLOW", "Tổng kg vượt giới hạn."); }
            StorefrontValues.Require(totalWeight < StorefrontValues.QuantityLimit, "WEIGHT_OVERFLOW", "Tổng kg vượt giới hạn.");
        }
        decimal steps;
        try { steps = rule.ByWeight ? decimal.Ceiling(Math.Max(0, totalWeight - rule.FirstWeightKg) / rule.AdditionalStepKg) : 0m; }
        catch (OverflowException) { throw new StorefrontRuleException("WEIGHT_RULE_INVALID", "Bước tăng kg tạo kết quả vượt giới hạn."); }
        var free = rule.FreeFromGoodsAmount.HasValue && goodsAmount >= rule.FreeFromGoodsAmount.Value;
        decimal fee;
        try { fee = free ? 0 : checked(rule.BaseFee + steps * rule.AdditionalFee); }
        catch (OverflowException) { throw new StorefrontRuleException("MONEY_INVALID", "Phí giao vượt giới hạn."); }
        StorefrontValues.Money(fee);
        var total = goodsAmount + fee;
        StorefrontValues.Money(total);
        return new(rule.RuleId, rule.Revision, goodsAmount, weightComplete ? totalWeight : null, steps, fee, total);
    }
}
