using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelPayloadAssembler
{
    public static ViettelInvoicePayloadDto Build(
        InvoiceHeadDto invoice,
        InvoiceProviderSetting setting,
        ViettelCorrectionInfo correctionInfo,
        ViettelSettingSnapshot snapshot,
        string transactionUuid,
        bool isCorrectionInvoice,
        List<ViettelItemInfoDto> itemInfo,
        ViettelSummaryBuildResult summaryResult)
    {
        return new ViettelInvoicePayloadDto
        {
            GeneralInvoiceInfo = ViettelGeneralInvoiceInfoBuilder.Build(
                invoice,
                setting,
                correctionInfo,
                snapshot.InvoiceType,
                snapshot.TemplateCode,
                snapshot.InvoiceSeries,
                transactionUuid,
                isCorrectionInvoice),

            BuyerInfo = ViettelBuyerInfoBuilder.Build(invoice),

            SellerInfo = new { },

            Payments = ViettelPaymentInfoBuilder.Build(setting),

            ItemInfo = itemInfo,

            Metadata = ViettelMetadataBuilder.Build(invoice),

            SummarizeInfo = summaryResult.SummarizeInfo,

            TaxBreakdowns = summaryResult.TaxBreakdowns
        };
    }
}