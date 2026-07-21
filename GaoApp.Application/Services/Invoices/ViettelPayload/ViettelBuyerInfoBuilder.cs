using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Constants;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelBuyerInfoBuilder
{
    public static ViettelBuyerInfoDto Build(InvoiceHeadDto invoice)
    {
        var buyerType = ViettelPayloadTextHelper.NormalizeBuyerType(invoice.BuyerType);

        var buyerName =
            ViettelPayloadTextHelper.NormalizeNullableText(invoice.BuyerName, 300)
            ?? string.Empty;

        var buyerLegalName =
            ViettelPayloadTextHelper.NormalizeNullableText(invoice.BuyerLegalName, 500);

        var buyerTaxCode =
            ViettelPayloadTextHelper.NormalizeBuyerTaxCodeForViettel(invoice.BuyerTaxCode);

        var buyerAddress =
            ViettelPayloadTextHelper.NormalizeNullableText(invoice.BuyerAddress, 1200);

        if (buyerType == InvoiceBuyerTypes.NoInvoice)
        {
            return new ViettelBuyerInfoDto
            {
                BuyerNotGetInvoice = 1,
                BuyerName = "Người mua không lấy hóa đơn",
                BuyerLegalName = null,
                BuyerTaxCode = null,
                BuyerAddressLine = null
            };
        }

        if (buyerType == InvoiceBuyerTypes.Individual)
        {
            return new ViettelBuyerInfoDto
            {
                BuyerNotGetInvoice = 0,

                // Cá nhân: dùng tên khách hàng.
                BuyerName = buyerName,

                // Cá nhân không dùng tên đơn vị.
                BuyerLegalName = null,

                BuyerTaxCode = string.IsNullOrWhiteSpace(buyerTaxCode)
                    ? null
                    : buyerTaxCode,

                BuyerAddressLine = buyerAddress
            };
        }

        // Business / doanh nghiệp / tổ chức / hộ kinh doanh.
        return new ViettelBuyerInfoDto
        {
            BuyerNotGetInvoice = 0,

            // Người liên hệ/người mua hàng.
            BuyerName = buyerName,

            // Tên pháp lý của đơn vị/công ty.
            BuyerLegalName = buyerLegalName,

            BuyerTaxCode = string.IsNullOrWhiteSpace(buyerTaxCode)
                ? null
                : buyerTaxCode,

            BuyerAddressLine = buyerAddress
        };
    }
}