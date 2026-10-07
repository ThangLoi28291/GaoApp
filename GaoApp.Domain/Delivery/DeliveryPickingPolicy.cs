using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace GaoApp.Domain.Delivery;

// Exact arithmetic for the persisted delivery quote; never reprice the source POS cart.
public static class DeliveryPickingPolicy
{
    private static readonly Regex QuantityText = new("^[0-9]+(?:\\.[0-9]{1,4})?$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    public static decimal ParseQuantity(string? text)
    {
        DeliveryValues.Require(text is not null && text.Length <= 19 && QuantityText.IsMatch(text) &&
            decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
            "QUANTITY_INVALID", "Số lượng phải là chữ số với tối đa 4 số lẻ, dùng dấu chấm.");
        var quantity = decimal.Parse(text!, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        DeliveryValues.Quantity(quantity, "Số lượng");
        return quantity;
    }
    public static decimal ToBase(decimal quantity, decimal multiplier)
    {
        DeliveryValues.Quantity(quantity, "Số lượng");
        DeliveryValues.Require(multiplier > 0 && multiplier < 1000000000000m && decimal.Round(multiplier, 6) == multiplier,
            "MULTIPLIER_INVALID", "Hệ số phải dương và tối đa 6 số lẻ.");
        var product = Scaled(quantity, 10000) * Scaled(multiplier, 1000000);
        var baseScaled = BigInteger.DivRem(product, 1000000, out var remainder);
        DeliveryValues.Require(remainder.IsZero && baseScaled < BigInteger.Parse("1000000000000000000", CultureInfo.InvariantCulture),
            "BASE_QUANTITY_INVALID", "Quy đổi cơ sở vượt giới hạn hoặc cần hơn 4 số lẻ.");
        return (decimal)baseScaled / 10000m;
    }
    public static decimal Prorate(decimal frozenNet, decimal orderedQuantity, decimal allowedQuantity)
    {
        DeliveryValues.Money(frozenNet, "Giá trị cố định");
        DeliveryValues.Quantity(orderedQuantity, "Lượng chụp");
        DeliveryValues.Quantity(allowedQuantity, "Lượng cho phép");
        DeliveryValues.Require(orderedQuantity > 0 && allowedQuantity <= orderedQuantity, "QUANTITY_EXCEEDS_SOURCE", "Lượng cho phép vượt snapshot.");
        return WholeDong(new BigInteger(frozenNet) * Scaled(allowedQuantity, 10000), Scaled(orderedQuantity, 10000));
    }
    public static decimal QuotedNet(decimal quantity, decimal unitPrice)
    {
        DeliveryValues.Quantity(quantity, "Lượng thay thế");
        DeliveryValues.Money(unitPrice, "Đơn giá", wholeDong: false);
        return WholeDong(Scaled(quantity, 10000) * Scaled(unitPrice, 100), 1000000);
    }
    public static void RequireReported(decimal planned, decimal? reported, string? reason)
    {
        DeliveryValues.Require(reported.HasValue, "PICKING_INCOMPLETE", "Phải ghi kết quả soạn từng dòng.");
        DeliveryValues.Quantity(planned, "Lượng dự kiến"); DeliveryValues.Quantity(reported!.Value, "Lượng đã soạn");
        DeliveryValues.Require(reported <= planned, "QUANTITY_EXCEEDS_SOURCE", "Lượng soạn vượt kế hoạch.");
        DeliveryValues.Require(reported > 0 && reported == planned || !string.IsNullOrWhiteSpace(reason),
            "SHORTAGE_REASON_REQUIRED", "Lượng không đủ hoặc bằng 0 cần lý do.");
    }
    public static void RequireRootCoverage(decimal originalOrdered, decimal retained, IEnumerable<decimal> replacements)
    {
        DeliveryValues.Quantity(originalOrdered, "Lượng gốc"); DeliveryValues.Quantity(retained, "Lượng giữ");
        var sum = Scaled(retained, 10000);
        foreach (var coverage in replacements) { DeliveryValues.Quantity(coverage, "Lượng bù"); sum += Scaled(coverage, 10000); }
        DeliveryValues.Require(sum <= Scaled(originalOrdered, 10000), "ROOT_COVERAGE_EXCEEDED", "Lượng giữ và thay thế vượt yêu cầu gốc.");
    }
    public static DeliveryCapability ForOperation(string operation) => operation switch
    {
        "claim" or "report" or "submit" => DeliveryCapability.Pick,
        "plan" or "approve" or "reopen" or "reassign" => DeliveryCapability.ApproveChanges,
        _ => throw new DeliveryRuleException("COMMAND_INVALID", "Thao tác soạn không hợp lệ.")
    };
    public static void EnsureState(DeliveryState state, string operation)
    {
        var allowed = operation switch
        {
            "claim" => state == DeliveryState.Created,
            "report" or "submit" => state == DeliveryState.Picking,
            "plan" or "reassign" => state is DeliveryState.Picking or DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover,
            "approve" => state == DeliveryState.AwaitingApproval,
            "reopen" => state is DeliveryState.AwaitingApproval or DeliveryState.ReadyForHandover,
            _ => false
        };
        DeliveryValues.Require(allowed, "STATE_CONFLICT", "Không được thao tác soạn ở trạng thái hiện tại.");
    }
    public static string FormatQuantity(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    public static string FormatMultiplier(decimal value) => value.ToString("0.######", CultureInfo.InvariantCulture);
    public static string FormatPrice(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    public static string FormatMoney(decimal value) => value.ToString("0", CultureInfo.InvariantCulture);
    private static BigInteger Scaled(decimal value, int scale) => new(value * scale);
    private static decimal WholeDong(BigInteger numerator, BigInteger denominator)
    {
        var quotient = BigInteger.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 >= denominator) quotient++;
        DeliveryValues.Require(quotient >= 0 && quotient < BigInteger.Parse("10000000000000000", CultureInfo.InvariantCulture),
            "MONEY_INVALID", "Giá trị vượt giới hạn lưu trữ.");
        return (decimal)quotient;
    }
}
