using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelCorrectionInfoBuilder
{
    public static ViettelCorrectionInfo Build(InvoiceHeadDto invoice)
    {
        if (!invoice.CorrectionType.HasValue)
        {
            return new ViettelCorrectionInfo
            {
                AdjustmentType = "1"
            };
        }

        if (string.IsNullOrWhiteSpace(invoice.OriginalInvoiceNo))
        {
            throw new InvalidOperationException("Hóa đơn thay thế/điều chỉnh thiếu số hóa đơn gốc.");
        }

        if (!invoice.OriginalInvoiceIssuedAtUtc.HasValue)
        {
            throw new InvalidOperationException("Hóa đơn thay thế/điều chỉnh thiếu ngày phát hành hóa đơn gốc.");
        }

        if (string.IsNullOrWhiteSpace(invoice.AdjustedNote))
        {
            throw new InvalidOperationException("Hóa đơn thay thế/điều chỉnh thiếu lý do sai sót.");
        }

        if (string.IsNullOrWhiteSpace(invoice.AdditionalReferenceDesc))
        {
            throw new InvalidOperationException("Hóa đơn thay thế/điều chỉnh thiếu thông tin văn bản thỏa thuận.");
        }

        if (!invoice.AdditionalReferenceDateUtc.HasValue)
        {
            throw new InvalidOperationException("Hóa đơn thay thế/điều chỉnh thiếu ngày văn bản thỏa thuận.");
        }

        var result = new ViettelCorrectionInfo
        {
            OriginalInvoiceId = ViettelPayloadTextHelper.NormalizeOriginalInvoiceNo(invoice.OriginalInvoiceNo),
            OriginalInvoiceIssueDate = ViettelPayloadTextHelper.ToUnixMilliseconds(invoice.OriginalInvoiceIssuedAtUtc.Value),
            AdjustedNote = ViettelPayloadTextHelper.TrimMax(invoice.AdjustedNote, 255),
            AdditionalReferenceDesc = ViettelPayloadTextHelper.TrimMax(invoice.AdditionalReferenceDesc, 225),
            AdditionalReferenceDate = ViettelPayloadTextHelper.ToUnixMilliseconds(invoice.AdditionalReferenceDateUtc.Value)
        };

        if (invoice.CorrectionType == InvoiceCorrectionType.Replacement)
        {
            result.AdjustmentType = "3";
            result.AdjustmentInvoiceType = null;
            return result;
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentAmount)
        {
            result.AdjustmentType = "5";
            result.AdjustmentInvoiceType = "1";
            return result;
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentInfo)
        {
            result.AdjustmentType = "5";
            result.AdjustmentInvoiceType = "2";
            return result;
        }

        throw new InvalidOperationException($"Loại hóa đơn xử lý sai sót không hợp lệ: {invoice.CorrectionType}");
    }
}

internal class ViettelCorrectionInfo
{
    public string AdjustmentType { get; set; } = "1";

    public string? AdjustmentInvoiceType { get; set; }

    public string? OriginalInvoiceId { get; set; }

    public long? OriginalInvoiceIssueDate { get; set; }

    public string? AdjustedNote { get; set; }

    public string? AdditionalReferenceDesc { get; set; }

    public long? AdditionalReferenceDate { get; set; }
}