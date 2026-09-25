using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoicePayloadBuilder : IViettelInvoicePayloadBuilder
{
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public ViettelInvoicePayloadBuilder(
        IInvoiceService invoiceService,
        IInvoiceProviderSettingRepository settingRepository,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _invoiceService = invoiceService;
        _settingRepository = settingRepository;
        _logRepository = logRepository;
    }

    public Task<Result<ViettelInvoicePayloadResultDto>> BuildAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
        => BuildCoreAsync(invoiceHeadId, null, null, ct);

    public Task<Result<ViettelInvoicePayloadResultDto>> BuildForIssueAsync(
        int invoiceHeadId, InvoiceProviderSetting setting, string transactionUuid,
        CancellationToken ct = default)
        => BuildCoreAsync(invoiceHeadId, setting, transactionUuid, ct);

    private async Task<Result<ViettelInvoicePayloadResultDto>> BuildCoreAsync(
        int invoiceHeadId, InvoiceProviderSetting? selectedSetting, string? issueUuid,
        CancellationToken ct)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoiceResult = await _invoiceService.GetInvoiceDetailAsync(
            invoiceHeadId,
            ct);

        if (!invoiceResult.IsSuccess)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                invoiceResult.Error!);
        }

        var invoice = invoiceResult.Value;
        var useCurrentSetting = LegacyInvoiceIssueConfiguration.UseCurrent(invoice);
        if (invoice.LegacyReadOnly || (invoice.LegacySourceId.HasValue && !invoice.InvoiceProviderSettingId.HasValue && !useCurrentSetting))
            return Result<ViettelInvoicePayloadResultDto>.Failure(Error.Validation("Invoice.LegacyNeedsReview",
                invoice.LegacyReadOnly ? "Hóa đơn GaoStore này chỉ lưu để tra cứu."
                : "Chưa ánh xạ cấu hình phát hành của hóa đơn GaoStore."));

        var setting = selectedSetting ?? await _settingRepository.GetForInvoiceAsync(
            invoice.StoreId,
            useCurrentSetting ? null : invoice.InvoiceProviderSettingId,
            ct);

        if (setting == null || setting.StoreId != invoice.StoreId || !setting.IsActive || setting.IsDeleted
            || !string.Equals(setting.ProviderCode, "VIETTEL", StringComparison.OrdinalIgnoreCase))
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng cho cửa hàng này."));
        }

        if (useCurrentSetting)
            LegacyInvoiceIssueConfiguration.Apply(invoice, setting);

        var temporaryUuid = issueUuid == null && useCurrentSetting && string.IsNullOrWhiteSpace(invoice.TransactionUuid);
        if (issueUuid != null)
            invoice.TransactionUuid = issueUuid;
        else if (temporaryUuid)
            invoice.TransactionUuid = Guid.NewGuid().ToString("D"); // Preview DTO only; never persisted here.

        var snapshotResult = ViettelSettingSnapshotBuilder.Build(
            invoice,
            setting);

        if (!snapshotResult.IsSuccess)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                snapshotResult.Error!);
        }

        var snapshot = snapshotResult.Value;

        var preBuildValidationResult =
            ViettelInvoicePreBuildValidator.Validate(invoice);

        if (!preBuildValidationResult.IsSuccess)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                preBuildValidationResult.Error!);
        }

        var preBuild = preBuildValidationResult.Value;

        var warnings = new List<string>();
        if (useCurrentSetting)
            warnings.Add("Hóa đơn nháp GaoStore sử dụng cấu hình Viettel đang hoạt động của cửa hàng.");
        if (temporaryUuid)
            warnings.Add("Mã giao dịch trong JSON này chỉ dùng xem trước; mã phát hành sẽ được lưu khi gửi hóa đơn.");

        var itemInfo = ViettelItemInfoBuilder.Build(
            preBuild.ActiveDetails,
            warnings,
            invoice.CorrectionType);

        var summaryResult = ViettelSummaryInfoBuilder.Build(itemInfo);

        AddTotalMismatchWarnings(
            invoice,
            summaryResult,
            warnings);

        ViettelCorrectionInfo correctionInfo;

        try
        {
            correctionInfo = ViettelCorrectionInfoBuilder.Build(invoice);
        }
        catch (InvalidOperationException ex)
        {
            return Result<ViettelInvoicePayloadResultDto>.Failure(
                Error.Validation(
                    "Invoice.CorrectionInfoInvalid",
                    ex.Message));
        }

        var payload = ViettelPayloadAssembler.Build(
            invoice: invoice,
            setting: setting,
            correctionInfo: correctionInfo,
            snapshot: snapshot,
            transactionUuid: preBuild.TransactionUuid,
            isCorrectionInvoice: preBuild.IsCorrectionInvoice,
            itemInfo: itemInfo,
            summaryResult: summaryResult);

        var json = ViettelPayloadJsonSerializer.SerializePretty(payload);

        var latestLogs = await _logRepository.GetLatestByInvoiceHeadAsync(
            invoiceHeadId,
            20,
            ct);

        var uiState = ViettelInvoiceUiStateBuilder.Build(invoice);
        if (temporaryUuid)
            uiState.CanSyncByUuid = false;

        var result = ViettelInvoicePayloadResultAssembler.Build(
            invoice: invoice,
            snapshot: snapshot,
            transactionUuid: preBuild.TransactionUuid,
            summaryResult: summaryResult,
            warnings: warnings,
            payload: payload,
            json: json,
            latestLogs: latestLogs,
            uiState: uiState);

        return Result<ViettelInvoicePayloadResultDto>.Success(result);
    }

    private static void AddTotalMismatchWarnings(
        InvoiceHeadDto invoice,
        ViettelSummaryBuildResult summaryResult,
        List<string> warnings)
    {
        var expectedSubTotal = ViettelPayloadTextHelper.RoundVnd(invoice.SubTotal);
        var expectedVatAmount = ViettelPayloadTextHelper.RoundVnd(invoice.VatAmount);
        var expectedGrandTotal = ViettelPayloadTextHelper.RoundVnd(invoice.GrandTotal);

        if (expectedSubTotal != summaryResult.TotalAmountWithoutTax)
        {
            warnings.Add(
                $"SubTotal trên InvoiceHead ({invoice.SubTotal:N0}) khác tổng dòng build JSON ({summaryResult.TotalAmountWithoutTax:N0}).");
        }

        if (expectedVatAmount != summaryResult.TotalTaxAmount)
        {
            warnings.Add(
                $"VatAmount trên InvoiceHead ({invoice.VatAmount:N0}) khác tổng VAT build JSON ({summaryResult.TotalTaxAmount:N0}).");
        }

        if (expectedGrandTotal != summaryResult.TotalAmountWithTax)
        {
            warnings.Add(
                $"GrandTotal trên InvoiceHead ({invoice.GrandTotal:N0}) khác tổng tiền build JSON ({summaryResult.TotalAmountWithTax:N0}).");
        }
    }
}
