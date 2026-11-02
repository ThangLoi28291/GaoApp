using GaoApp.Application.Common.Results;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public static class InvoiceRevenueTaxPolicy
{
    public static bool IsValidPercentage(int? percentage) => percentage is 1 or 2 or 3 or 5;

    public static decimal Reduction(decimal amount, int? percentage)
        => percentage.HasValue
            ? Math.Round(amount * percentage.Value / 500m, 0, MidpointRounding.AwayFromZero)
            : 0m;

    public static decimal Gross(IEnumerable<decimal> amounts)
        => amounts.Sum(x => Math.Round(x, 0, MidpointRounding.AwayFromZero));

    public static Error? Validate(int? percentage, string? invoiceType, InvoiceCorrectionType? correctionType,
        DateTime invoiceDate, bool hasVat)
    {
        if (!percentage.HasValue) return null;
        if (!IsValidPercentage(percentage))
            return Error.Validation("Invoice.RevenueTaxPercentageInvalid", "Chọn tỷ lệ theo doanh thu 1%, 2%, 3% hoặc 5%.");
        if (invoiceType?.Trim() is not ("2" or "02GTTT"))
            return Error.Validation("Invoice.RevenueTaxSalesInvoiceRequired", "Giảm 20% tỷ lệ thuế theo doanh thu chỉ áp dụng cho hóa đơn bán hàng (loại 2), không áp dụng cho hóa đơn GTGT.");
        if (correctionType is InvoiceCorrectionType.AdjustmentAmount or InvoiceCorrectionType.AdjustmentInfo)
            return Error.Validation("Invoice.RevenueTaxCorrectionUnsupported", "Lựa chọn giảm thuế này dành cho hóa đơn gốc hoặc hóa đơn thay thế.");
        if (invoiceDate.Date < new DateTime(2025, 7, 1) || invoiceDate.Date > new DateTime(2026, 12, 31))
            return Error.Validation("Invoice.RevenueTaxOutsidePeriod", "Chính sách theo Nghị định 174/2025/NĐ-CP áp dụng từ 01/07/2025 đến hết 31/12/2026.");
        if (hasVat)
            return Error.Validation("Invoice.RevenueTaxWithVat", "Hóa đơn bán hàng giảm tỷ lệ thuế theo doanh thu phải có VAT các dòng bằng 0. Kiểm tra loại hóa đơn và các dòng hàng trước khi bật.");
        return null;
    }
}
