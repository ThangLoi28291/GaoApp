namespace GaoApp.Application.Services.Purchases;

public static class PurchasePricingPolicy
{
    public readonly record struct LineAmounts(
        decimal UnitPriceBeforeVat,
        decimal UnitPriceAfterVat,
        decimal TaxRate,
        decimal LineTotalBeforeVat,
        decimal VatAmount,
        decimal LineTotalAfterVat);

    public static LineAmounts CalculateLine(
        decimal quantity,
        decimal unitPriceAfterVat,
        bool hasVat,
        decimal taxRate)
    {
        if (quantity <= 0) throw new InvalidOperationException("Số lượng mua phải lớn hơn 0.");
        if (unitPriceAfterVat < 0) throw new InvalidOperationException("Đơn giá mua không được âm.");
        if (taxRate is < 0 or > 100) throw new InvalidOperationException("Thuế suất phải từ 0% đến 100%.");

        var afterUnit = RoundMoney(unitPriceAfterVat);
        var appliedRate = hasVat ? taxRate : 0m;
        var afterTotal = RoundMoney(quantity * afterUnit);

        if (!hasVat || appliedRate == 0m)
        {
            return new LineAmounts(afterUnit, afterUnit, 0m, afterTotal, 0m, afterTotal);
        }

        var divisor = 1m + (appliedRate / 100m);
        var beforeUnit = RoundMoney(afterUnit / divisor);
        var beforeTotal = RoundMoney(afterTotal / divisor);
        var vat = RoundMoney(afterTotal - beforeTotal);
        return new LineAmounts(beforeUnit, afterUnit, appliedRate, beforeTotal, vat, afterTotal);
    }

    /// <summary>
    /// Calculates purchase amounts when the entered unit price is exclusive of VAT.
    /// This is intentionally separate from <see cref="CalculateLine"/> because legacy
    /// purchase orders and direct receipts still enter a VAT-inclusive unit price.
    /// </summary>
    public static LineAmounts CalculateLineFromBeforeVat(
        decimal quantity,
        decimal unitPriceBeforeVat,
        bool hasVat,
        decimal taxRate)
    {
        if (quantity <= 0) throw new InvalidOperationException("Số lượng mua phải lớn hơn 0.");
        if (unitPriceBeforeVat < 0) throw new InvalidOperationException("Đơn giá mua không được âm.");
        if (taxRate is < 0 or > 100) throw new InvalidOperationException("Thuế suất phải từ 0% đến 100%.");

        var beforeUnit = RoundMoney(unitPriceBeforeVat);
        var appliedRate = hasVat ? taxRate : 0m;
        var beforeTotal = RoundMoney(quantity * beforeUnit);

        if (!hasVat || appliedRate == 0m)
        {
            return new LineAmounts(beforeUnit, beforeUnit, 0m, beforeTotal, 0m, beforeTotal);
        }

        var vat = RoundMoney(beforeTotal * appliedRate / 100m);
        var afterTotal = RoundMoney(beforeTotal + vat);
        var afterUnit = RoundMoney(beforeUnit * (1m + appliedRate / 100m));
        return new LineAmounts(beforeUnit, afterUnit, appliedRate, beforeTotal, vat, afterTotal);
    }

    /// <summary>
    /// Converts a historical VAT-inclusive suggested cost into the exclusive price
    /// expected by the purchase-request conversion form.
    /// </summary>
    public static decimal CalculateUnitPriceBeforeVatFromAfterVat(
        decimal unitPriceAfterVat,
        bool hasVat,
        decimal taxRate)
    {
        if (unitPriceAfterVat < 0) throw new InvalidOperationException("Đơn giá mua không được âm.");
        if (taxRate is < 0 or > 100) throw new InvalidOperationException("Thuế suất phải từ 0% đến 100%.");

        var afterUnit = RoundMoney(unitPriceAfterVat);
        if (!hasVat || taxRate == 0m) return afterUnit;

        return RoundMoney(afterUnit / (1m + taxRate / 100m));
    }

    public static IReadOnlyDictionary<int, decimal> AllocateFreight(
        decimal freightTotal,
        IReadOnlyList<(int LineId, decimal LineAmountAfterVat)> lines)
    {
        var total = RoundMoney(freightTotal);
        if (total < 0) throw new InvalidOperationException("Phí vận chuyển không được âm.");
        if (lines.Count == 0) return new Dictionary<int, decimal>();
        if (lines.Any(x => x.LineAmountAfterVat < 0))
            throw new InvalidOperationException("Thành tiền dòng không hợp lệ.");

        var weightTotal = lines.Sum(x => x.LineAmountAfterVat);
        if (total > 0 && weightTotal <= 0)
            throw new InvalidOperationException("Không thể phân bổ phí khi tổng tiền hàng bằng 0.");

        var result = new Dictionary<int, decimal>();
        decimal allocated = 0m;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var amount = index == lines.Count - 1
                ? RoundMoney(total - allocated)
                : RoundMoney(total * line.LineAmountAfterVat / weightTotal);
            result[line.LineId] = amount;
            allocated += amount;
        }

        return result;
    }

    public static void EnsureFreightBalanced(decimal freightTotal, IEnumerable<decimal> allocations)
    {
        var materialized = allocations.ToArray();
        if (materialized.Any(x => x < 0m))
            throw new InvalidOperationException("Phí vận chuyển phân bổ không được âm.");

        var difference = RoundMoney(materialized.Sum()) - RoundMoney(freightTotal);
        if (difference != 0m)
        {
            throw new InvalidOperationException(
                difference < 0
                    ? $"Tổng phân bổ phí vận chuyển còn thiếu {Math.Abs(difference):N0} đ."
                    : $"Tổng phân bổ phí vận chuyển đang vượt {difference:N0} đ.");
        }
    }

    public static decimal CalculateBaseUnitCost(
        decimal merchandiseAmountBeforeVat,
        decimal vatAmount,
        decimal freightAllocation,
        decimal baseQuantity,
        bool includeVatInInventoryCost,
        bool capitalizeFreightInInventoryCost)
    {
        if (baseQuantity <= 0) throw new InvalidOperationException("Số lượng quy đổi phải lớn hơn 0.");
        if (merchandiseAmountBeforeVat < 0m)
            throw new InvalidOperationException("Tiền hàng trước VAT không được âm.");
        if (vatAmount < 0m)
            throw new InvalidOperationException("Tiền VAT không được âm.");
        if (freightAllocation < 0m)
            throw new InvalidOperationException("Phí vận chuyển phân bổ không được âm.");

        var inventoryValue = checked(
            RoundMoney(merchandiseAmountBeforeVat) +
            (includeVatInInventoryCost ? RoundMoney(vatAmount) : 0m) +
            (capitalizeFreightInInventoryCost ? RoundMoney(freightAllocation) : 0m));
        return decimal.Round(
            inventoryValue / baseQuantity,
            6,
            MidpointRounding.AwayFromZero);
    }

    public static decimal RoundMoney(decimal value)
        => decimal.Round(value, 2, MidpointRounding.AwayFromZero);

    public static decimal RoundQuantity(decimal value)
        => decimal.Round(value, 3, MidpointRounding.AwayFromZero);
}
