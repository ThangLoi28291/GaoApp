using System.Globalization;

namespace GaoApp.Domain.Delivery;

// ReportedReturn is a cumulative declaration, including goods already accepted by the warehouse.
public sealed record DeliveryOutcomeQuantity(int LineId, decimal Dispatched, decimal AcceptedReturn,
    decimal CurrentCustody, decimal Delivered, decimal ReportedReturn);

public static class DeliveryOutcomePolicy
{
    public static decimal ParseQuantity(string text)
    {
        DeliveryValues.Require(!string.IsNullOrEmpty(text) && text.Length <= 24 &&
            decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
            "QUANTITY_INVALID", "Cần số lượng chuẩn theo đơn vị cơ sở.");
        var value = decimal.Parse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        DeliveryValues.Quantity(value, "Số lượng báo cáo");
        DeliveryValues.Require(value.ToString("0.####", CultureInfo.InvariantCulture) == text,
            "QUANTITY_INVALID", "Số lượng phải dùng chuỗi chuẩn, tối đa 4 số lẻ.");
        return value;
    }

    public static decimal ParseMoney(string text)
    {
        DeliveryValues.Require(!string.IsNullOrEmpty(text) && text.Length <= 16 && text.All(char.IsAsciiDigit) &&
            decimal.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out _),
            "MONEY_INVALID", "Số tiền người giao báo phải là số đồng nguyên không âm.");
        var value = decimal.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        DeliveryValues.Money(value, "Số tiền người giao báo");
        DeliveryValues.Require(value.ToString("0", CultureInfo.InvariantCulture) == text,
            "MONEY_INVALID", "Số tiền phải dùng chuỗi số đồng nguyên chuẩn.");
        return value;
    }

    public static string? Validate(string outcome, string? reason, IReadOnlyList<DeliveryOutcomeQuantity> lines,
        decimal reportedCash, decimal reportedTransfer)
    {
        DeliveryValues.Require(outcome is "Full" or "Partial" or "NotDelivered" or "Refused" or "Unreachable" or "Cancelled",
            "OUTCOME_INVALID", "Chọn kết quả giao hàng hợp lệ.");
        DeliveryValues.Require(lines is not null && lines.Count > 0 && lines.All(x => x is not null && x.LineId > 0) &&
            lines.Select(x => x.LineId).Distinct().Count() == lines.Count,
            "LINE_INVALID", "Cần đúng các dòng hàng đã xuất, không trùng.");
        var validatedLines = lines!;
        foreach (var line in validatedLines)
        {
            DeliveryValues.Quantity(line.Dispatched, "Đã xuất");
            DeliveryValues.Quantity(line.AcceptedReturn, "Đã nhận trả");
            DeliveryValues.Quantity(line.CurrentCustody, "Trách nhiệm giữ hàng");
            DeliveryValues.Quantity(line.Delivered, "Báo đã giao");
            DeliveryValues.Quantity(line.ReportedReturn, "Báo mang về cộng dồn");
            DeliveryValues.Require(line.Dispatched > 0 && line.AcceptedReturn + line.CurrentCustody == line.Dispatched,
                "CUSTODY_PROOF_INVALID", "Trách nhiệm giữ hàng không khớp xuất/nhận trả thật.");
            DeliveryValues.Require(line.Delivered + line.ReportedReturn == line.Dispatched &&
                line.ReportedReturn >= line.AcceptedReturn && line.Delivered <= line.CurrentCustody,
                "OUTCOME_QUANTITY_INVALID", "Đã giao và tổng báo mang về phải khớp xuất; không báo giao hàng đã nhận trả.");
        }
        var full = validatedLines.All(x => x.Delivered == x.Dispatched);
        var anyDelivered = validatedLines.Any(x => x.Delivered > 0);
        DeliveryValues.Require(outcome == "Full" ? full : outcome == "Partial" ? anyDelivered && !full : !anyDelivered,
            "OUTCOME_QUANTITY_INVALID", "Kết quả giao không khớp số lượng đã khai báo.");
        var normalized = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        DeliveryValues.Require(normalized is null || normalized.Length <= 1000 &&
            !normalized.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')),
            "REASON_INVALID", "Lý do tối đa 1000 ký tự, không chứa ký tự điều khiển.");
        DeliveryValues.Require(outcome == "Full" || normalized is not null,
            "REASON_REQUIRED", "Kết quả không giao đủ cần lý do rõ ràng.");
        // Declarations may reveal a discrepancy even when no goods were delivered. No posting follows.
        DeliveryValues.Money(reportedCash, "Tiền mặt người giao báo");
        DeliveryValues.Money(reportedTransfer, "Chuyển khoản người giao báo");
        return normalized;
    }
}
