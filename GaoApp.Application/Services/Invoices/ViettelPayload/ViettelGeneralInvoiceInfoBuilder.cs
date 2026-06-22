using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelGeneralInvoiceInfoBuilder
{
    public static ViettelGeneralInvoiceInfoDto Build(
        InvoiceHeadDto invoice,
        InvoiceProviderSetting setting,
        ViettelCorrectionInfo correctionInfo,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        string transactionUuid,
        bool isCorrectionInvoice)
    {
        return new ViettelGeneralInvoiceInfoDto
        {
            InvoiceType = invoiceType,
            TemplateCode = templateCode,
            InvoiceSeries = invoiceSeries,

            CurrencyCode = string.IsNullOrWhiteSpace(setting.CurrencyCode)
                ? "VND"
                : setting.CurrencyCode.Trim(),

            AdjustmentType = correctionInfo.AdjustmentType,
            AdjustmentInvoiceType = correctionInfo.AdjustmentInvoiceType,
            OriginalInvoiceId = correctionInfo.OriginalInvoiceId,
            OriginalInvoiceIssueDate = correctionInfo.OriginalInvoiceIssueDate,
            AdjustedNote = correctionInfo.AdjustedNote,
            AdditionalReferenceDesc = correctionInfo.AdditionalReferenceDesc,
            AdditionalReferenceDate = correctionInfo.AdditionalReferenceDate,

            PaymentStatus = setting.DefaultPaymentStatus,
            CusGetInvoiceRight = setting.CusGetInvoiceRight,
            TransactionUuid = transactionUuid,

            InvoiceIssuedDate = isCorrectionInvoice
                ? DateTimeOffset.Now.ToUnixTimeMilliseconds()
                : null,

            InvoiceNote = string.IsNullOrWhiteSpace(invoice.Note)
                ? null
                : ViettelPayloadTextHelper.TrimMax(invoice.Note, 500),

            Validation = 0
        };
    }
}