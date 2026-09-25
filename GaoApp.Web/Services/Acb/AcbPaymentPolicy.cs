using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Web.Services.Acb;

public static class AcbPaymentPolicy
{
    public const string CartChangedReason = "Đơn hàng đã thay đổi sau khi tạo QR.";
    private static readonly JsonSerializerOptions FingerprintOptions = new()
    {
        Converters = { new NumericDecimalConverter() }
    };
    public static bool Eligible(Order order)
    {
        var lines = order.Lines.Where(x => !x.IsDeleted).ToList();
        // Gift lines deliberately participate in the eligibility check.
        return lines.Count > 0 && lines.All(x => x.Variant?.HasInputInvoice == true);
    }
    public static decimal Balance(Order order) => order.GrandTotal - order.Payments.Where(x => !x.IsDeleted).Sum(x => x.Amount);
    // Decimal scale changes between POS Recalc (9000) and SQL decimal(18,2) (9000.00).
    // Hash numeric values, preserving every significant digit, instead of their incidental scale.
    public static string Fingerprint(Order order) => Hash(JsonSerializer.Serialize(Snapshot(order), FingerprintOptions));

    public static bool MatchesFingerprint(Order order, string expected) =>
        Fingerprint(order) == expected ||
        Hash(JsonSerializer.Serialize(Snapshot(order))) == expected ||
        Hash(JsonSerializer.Serialize(Snapshot(order, sqlScales: true))) == expected ||
        Hash(JsonSerializer.Serialize(Snapshot(order, sqlScales: true, calculatedTotals: true))) == expected;

    private static object Snapshot(Order order, bool sqlScales = false, bool calculatedTotals = false) => new
    {
        GrandTotal = Scale(order.GrandTotal, calculatedTotals ? 0 : sqlScales ? 2 : null),
        order.CustomerId, order.POSShiftId,
        lines = order.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.Id).Select(x => new
        {
            x.Id, x.VariantId,
            Quantity = Scale(x.Quantity, sqlScales ? 4 : null),
            UnitPrice = Scale(x.UnitPrice, sqlScales ? 2 : null),
            LineDiscount = Scale(x.LineDiscount, sqlScales ? 2 : null),
            LineTotal = Scale(x.LineTotal, calculatedTotals ? 0 : sqlScales ? 2 : null)
        })
    };

    private static decimal Scale(decimal value, int? scale)
    {
        if (!scale.HasValue) return value;
        var formatted = decimal.Parse(value.ToString("F" + scale.Value, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        // Legacy compatibility must never hide rounding or an actual price/quantity change.
        return formatted == value ? formatted : value;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private sealed class NumericDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }
    public static string? ReviewReason(AcbQrSession session, Order order, IReadOnlyCollection<AcbPaymentTransaction> transactions)
    {
        var paid = transactions.Where(x => AcbProtocol.IsPaid(x.Status)).ToList();
        // Offline manual receipts have explicit cashier provenance. They do not claim bank evidence.
        var manuallyRecorded = session.ConfirmationSource == AcbConfirmationSource.OfflineManual && session.PaymentId.HasValue;
        if (paid.Count == 0 && !manuallyRecorded) return session.Status is AcbSessionStatus.Received or AcbSessionStatus.Completed ? "ACB không còn xác nhận giao dịch thành công; cần kiểm tra." : null;
        if (session.Status == AcbSessionStatus.Cancelled) return "Tiền về sau khi QR đã hủy.";
        if (paid.Count > 0 && (paid.Count != 1 || paid[0].Amount != session.Amount)) return "Số tiền hoặc số lần chuyển không khớp QR; cần kiểm tra.";
        if (order.POSShiftId != session.ShiftId || order.POSShift?.TerminalId != session.TerminalId)
            return "Đơn đã chuyển sang ca hoặc máy khác.";
        if (order.POSShift?.Status != POSShiftStatus.Open && session.Status != AcbSessionStatus.Completed &&
            !(order.Status == OrderStatus.Completed && session.PaymentId != null))
            return "Tiền về khi ca bán hàng đã đóng.";
        if (!MatchesFingerprint(order, session.CartFingerprint)) return CartChangedReason;
        if (session.PaymentId.HasValue && !order.Payments.Any(x => x.Id == session.PaymentId && !x.IsDeleted && x.Amount == session.Amount && x.ReferenceCode == session.ProviderOrderId))
            return "Khoản thanh toán ACB đã thay đổi; cần kiểm tra.";
        if (!session.PaymentId.HasValue && (order.Status != OrderStatus.Draft || Balance(order) < session.Amount))
            return "Đơn hoặc số tiền còn thiếu đã thay đổi; cần kiểm tra.";
        return null;
    }
}
