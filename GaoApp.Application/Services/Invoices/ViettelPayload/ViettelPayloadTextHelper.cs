using GaoApp.Domain.Constants;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelPayloadTextHelper
{
    public static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }

    public static decimal RoundVnd(decimal value)
    {
        return Math.Round(value, 0, MidpointRounding.AwayFromZero);
    }

    public static string TrimMax(string value, int maxLength)
    {
        value = (value ?? string.Empty).Trim();

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    public static string? NormalizeNullableText(string? value, int maxLength)
    {
        value = (value ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }

    public static string NormalizeBuyerType(string? buyerType)
    {
        buyerType = (buyerType ?? string.Empty).Trim();

        if (buyerType.Equals(InvoiceBuyerTypes.NoInvoice, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.NoInvoice;

        if (buyerType.Equals(InvoiceBuyerTypes.Individual, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.Individual;

        if (buyerType.Equals(InvoiceBuyerTypes.Business, StringComparison.OrdinalIgnoreCase))
            return InvoiceBuyerTypes.Business;

        return InvoiceBuyerTypes.NoInvoice;
    }

    public static string NormalizeBuyerTaxCodeForViettel(string? taxCode)
    {
        taxCode = (taxCode ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(taxCode))
            return string.Empty;

        // Không xóa dấu "-".
        // Ví dụ MST chi nhánh: 8341454784-001.
        return taxCode
            .Replace(" ", "")
            .Replace(".", "")
            .Trim()
            .ToUpperInvariant();
    }

    public static string? NormalizeInvoiceNote(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
            return null;

        note = note.Trim();

        // Ghi chú nội bộ hệ thống, không gửi lên Viettel.
        if (note.Contains("Tạo khung hóa đơn bán ra từ POS order", StringComparison.OrdinalIgnoreCase))
            return null;

        if (note.Contains("Chưa sinh dòng chi tiết", StringComparison.OrdinalIgnoreCase))
            return null;

        return TrimMax(note, 500);
    }

    public static long ToUnixMilliseconds(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        return new DateTimeOffset(utc).ToUnixTimeMilliseconds();
    }

    public static string NormalizeOriginalInvoiceNo(string? value)
    {
        value = (value ?? string.Empty).Trim();

        value = new string(value
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return value.Length <= 15
            ? value
            : value[^15..];
    }
}