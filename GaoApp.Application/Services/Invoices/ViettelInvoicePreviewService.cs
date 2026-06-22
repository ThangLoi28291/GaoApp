using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoicePreviewService : IViettelInvoicePreviewService
{
    private readonly IViettelInvoicePayloadBuilder _payloadBuilder;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IViettelInvoicePreviewClient _previewClient;

    public ViettelInvoicePreviewService(
        IViettelInvoicePayloadBuilder payloadBuilder,
        IInvoiceProviderSettingRepository settingRepository,
        IViettelInvoicePreviewClient previewClient)
    {
        _payloadBuilder = payloadBuilder;
        _settingRepository = settingRepository;
        _previewClient = previewClient;
    }

    public async Task<Result<ViettelInvoicePreviewFileDto>> PreviewDraftPdfAsync(
    int invoiceHeadId,
    CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var payloadResult = await _payloadBuilder.BuildAsync(invoiceHeadId, ct);

        if (!payloadResult.IsSuccess)
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(payloadResult.Error!);
        }

        var payloadData = payloadResult.Value;
        if (payloadData.StoreId <= 0)
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Invoice.StoreIdMissing",
                    "PayloadResult thiếu StoreId, không thể lấy cấu hình Viettel theo cửa hàng."));
        }

        var setting = await _settingRepository.GetActiveViettelAsync(
            payloadData.StoreId,
            ct);

        if (setting == null)
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng cho cửa hàng này."));
        }

        var supplierTaxCode = FirstNonEmpty(
            payloadData.SupplierTaxCode,
            setting.SupplierTaxCode);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            return Result<ViettelInvoicePreviewFileDto>.Failure(
                Error.Validation(
                    "Invoice.SupplierTaxCodeMissing",
                    "Hóa đơn thiếu MST phát hành, không thể preview Viettel."));
        }

        return await _previewClient.CreateDraftPreviewAsync(
            invoiceHeadId: invoiceHeadId,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: supplierTaxCode,
            payload: payloadData.Payload,
            ct: ct);
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}