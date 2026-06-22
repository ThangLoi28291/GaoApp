using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelSettingSnapshotBuilder
{
    public static Result<ViettelSettingSnapshot> Build(
        InvoiceHeadDto invoice,
        InvoiceProviderSetting setting)
    {
        var supplierTaxCode = ViettelPayloadTextHelper.FirstNonEmpty(
            invoice.SupplierTaxCode,
            setting.SupplierTaxCode);

        var invoiceType = ViettelPayloadTextHelper.FirstNonEmpty(
            invoice.InvoiceType,
            setting.InvoiceType,
            "1");

        var templateCode = ViettelPayloadTextHelper.FirstNonEmpty(
            invoice.TemplateCode,
            setting.TemplateCode);

        var invoiceSeries = ViettelPayloadTextHelper.FirstNonEmpty(
            invoice.InvoiceSeries,
            setting.InvoiceSeries);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelSettingSnapshot>.Failure(
                Error.Validation(
                    "Invoice.SupplierTaxCodeMissing",
                    "Hóa đơn thiếu MST phát hành Viettel."));
        }

        if (string.IsNullOrWhiteSpace(invoiceType))
        {
            return Result<ViettelSettingSnapshot>.Failure(
                Error.Validation(
                    "Invoice.InvoiceTypeMissing",
                    "Hóa đơn thiếu loại hóa đơn Viettel."));
        }

        if (string.IsNullOrWhiteSpace(templateCode))
        {
            return Result<ViettelSettingSnapshot>.Failure(
                Error.Validation(
                    "Invoice.TemplateCodeMissing",
                    "Hóa đơn thiếu mẫu số hóa đơn Viettel."));
        }

        if (string.IsNullOrWhiteSpace(invoiceSeries))
        {
            return Result<ViettelSettingSnapshot>.Failure(
                Error.Validation(
                    "Invoice.InvoiceSeriesMissing",
                    "Hóa đơn thiếu ký hiệu hóa đơn Viettel."));
        }

        return Result<ViettelSettingSnapshot>.Success(
            new ViettelSettingSnapshot
            {
                SupplierTaxCode = supplierTaxCode,
                InvoiceType = invoiceType,
                TemplateCode = templateCode,
                InvoiceSeries = invoiceSeries
            });
    }
}

internal class ViettelSettingSnapshot
{
    public string SupplierTaxCode { get; set; } = string.Empty;

    public string InvoiceType { get; set; } = "1";

    public string TemplateCode { get; set; } = string.Empty;

    public string InvoiceSeries { get; set; } = string.Empty;
}