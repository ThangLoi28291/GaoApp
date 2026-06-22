using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoiceIssueService : IViettelInvoiceIssueService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IInvoiceCorrectionRepository _correctionRepository;
    private readonly IViettelInvoicePayloadBuilder _payloadBuilder;
    private readonly IViettelInvoiceIssueClient _issueClient;

    public ViettelInvoiceIssueService(
        IInvoiceRepository invoiceRepository,
        IInvoiceProviderSettingRepository settingRepository,
        IInvoiceCorrectionRepository correctionRepository,
        IViettelInvoicePayloadBuilder payloadBuilder,
        IViettelInvoiceIssueClient issueClient)
    {
        _invoiceRepository = invoiceRepository;
        _settingRepository = settingRepository;
        _correctionRepository = correctionRepository;
        _payloadBuilder = payloadBuilder;
        _issueClient = issueClient;
    }

    public async Task<Result<ViettelInvoiceIssueResultDto>> IssueAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(
            invoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        var correctionCase = await GetCorrectionCaseIfAnyAsync(invoice, ct);

        if (IsAlreadyIssued(invoice))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Invoice.AlreadyIssued",
                    $"Hóa đơn này đã phát hành Viettel. Số hóa đơn: {invoice.ProviderInvoiceNo ?? invoice.InvoiceNumber}."));
        }

        if (MustSyncUuidBeforeIssue(invoice))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Invoice.MustSyncUuidBeforeIssue",
                    "Lần phát hành trước chưa rõ kết quả. Vui lòng bấm Tra cứu UUID trước khi phát hành lại."));
        }

        var amountValidation = ValidateInvoiceAmountBeforeIssue(invoice);
        if (!amountValidation.IsSuccess)
        {
            await MarkCorrectionFailedAsync(
                correctionCase,
                amountValidation.Error?.Code,
                amountValidation.Error?.Message,
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(amountValidation.Error!);
        }

        var detailValidation = ValidateInvoiceDetailsBeforeIssue(invoice);
        if (!detailValidation.IsSuccess)
        {
            await MarkCorrectionFailedAsync(
                correctionCase,
                detailValidation.Error?.Code,
                detailValidation.Error?.Message,
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(detailValidation.Error!);
        }

        if (string.IsNullOrWhiteSpace(invoice.TransactionUuid))
        {
            invoice.TransactionUuid = Guid.NewGuid().ToString("D");
        }

        var setting = await _settingRepository.GetActiveViettelAsync(
      invoice.StoreId,
      ct);

        if (setting == null)
        {
            await MarkCorrectionFailedAsync(
                correctionCase,
                "InvoiceProvider.NotConfigured",
                "Chưa có cấu hình Viettel đang dùng.",
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "InvoiceProvider.NotConfigured",
                    "Chưa có cấu hình Viettel đang dùng."));
        }

        var payloadResult = await _payloadBuilder.BuildAsync(invoiceHeadId, ct);

        if (!payloadResult.IsSuccess)
        {
            await MarkCorrectionFailedAsync(
                correctionCase,
                payloadResult.Error?.Code,
                payloadResult.Error?.Message,
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(payloadResult.Error!);
        }

        invoice.ProviderStatus = InvoiceProviderStatus.Issuing;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;
        invoice.LastSyncedAtUtc = DateTime.UtcNow;

        MarkCorrectionIssuing(correctionCase);

        await _invoiceRepository.SaveChangesAsync(ct);

        var payloadData = payloadResult.Value;

        var supplierTaxCode = FirstNonEmpty(
            invoice.SupplierTaxCode,
            payloadData.SupplierTaxCode,
            setting.SupplierTaxCode);

        if (string.IsNullOrWhiteSpace(supplierTaxCode))
        {
            await MarkCorrectionFailedAsync(
                correctionCase,
                "Invoice.SupplierTaxCodeMissing",
                "Hóa đơn thiếu MST phát hành.",
                ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    "Invoice.SupplierTaxCodeMissing",
                    "Hóa đơn thiếu MST phát hành."));
        }

        var issueResult = await _issueClient.IssueInvoiceAsync(
            invoiceHeadId: invoiceHeadId,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: supplierTaxCode,
            payload: payloadData.Payload,
            ct: ct);

        if (!issueResult.IsSuccess)
        {
            invoice.ProviderStatus = InvoiceProviderStatus.IssueFailed;
            invoice.LastErrorCode = issueResult.Error?.Code;
            invoice.LastErrorMessage = issueResult.Error?.Message;
            invoice.LastSyncedAtUtc = DateTime.UtcNow;

            MarkCorrectionFailed(
                correctionCase,
                issueResult.Error?.Code,
                issueResult.Error?.Message);

            await _invoiceRepository.SaveChangesAsync(ct);

            return issueResult;
        }

        var issue = issueResult.Value;

        if (!issue.IsSuccess)
        {
            invoice.ProviderStatus = InvoiceProviderStatus.IssueFailed;
            invoice.LastErrorCode = issue.ErrorCode;
            invoice.LastErrorMessage = issue.ErrorMessage;
            invoice.LastSyncedAtUtc = DateTime.UtcNow;

            MarkCorrectionFailed(
                correctionCase,
                issue.ErrorCode,
                issue.ErrorMessage);

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    issue.ErrorCode ?? "Viettel.IssueFailed",
                    issue.ErrorMessage ?? "Viettel phát hành hóa đơn thất bại."));
        }

        var issuedAtUtc = DateTime.UtcNow;

        invoice.ProviderStatus = string.IsNullOrWhiteSpace(issue.InvoiceNo)
            ? InvoiceProviderStatus.IssuedWaitingNumber
            : InvoiceProviderStatus.Issued;

        // Lưu snapshot cấu hình Viettel đúng thời điểm phát hành.
        // Không ghi đè snapshot nếu InvoiceHead đã có.
        // Chỉ bổ sung nếu dữ liệu cũ bị thiếu.
        invoice.ProviderCode = FirstNonEmpty(invoice.ProviderCode, "VIETTEL") ?? "VIETTEL";

        invoice.SupplierTaxCode = FirstNonEmpty(
            invoice.SupplierTaxCode,
            payloadData.SupplierTaxCode,
            setting.SupplierTaxCode);

        invoice.InvoiceType = FirstNonEmpty(
            invoice.InvoiceType,
            payloadData.Payload.GeneralInvoiceInfo?.InvoiceType,
            setting.InvoiceType,
            "1");

        invoice.TemplateCode = FirstNonEmpty(
            invoice.TemplateCode,
            payloadData.TemplateCode,
            setting.TemplateCode);

        invoice.InvoiceSeries = FirstNonEmpty(
            invoice.InvoiceSeries,
            payloadData.InvoiceSeries,
            setting.InvoiceSeries);

        invoice.ProviderInvoiceNo = issue.InvoiceNo;
        invoice.ProviderTransactionId = issue.TransactionId;
        invoice.ReservationCode = issue.ReservationCode;
        invoice.CodeOfTax = issue.CodeOfTax;

        if (!string.IsNullOrWhiteSpace(issue.InvoiceNo))
        {
            invoice.InvoiceNumber = issue.InvoiceNo;
        }

        invoice.IssuedAtUtc = issuedAtUtc;
        invoice.LastSyncedAtUtc = issuedAtUtc;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;

        invoice.IsLocked = true;
        invoice.LockedAtUtc ??= issuedAtUtc;
        invoice.LockReason = string.IsNullOrWhiteSpace(issue.InvoiceNo)
            ? "Đã gửi phát hành Viettel, đang chờ số hóa đơn."
            : $"Đã phát hành Viettel. Số hóa đơn: {issue.InvoiceNo}.";

        MarkCorrectionIssued(
            correctionCase,
            issuedAtUtc);

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<ViettelInvoiceIssueResultDto>.Success(issue);
    }

    private async Task<InvoiceCorrectionCase?> GetCorrectionCaseIfAnyAsync(
        InvoiceHead invoice,
        CancellationToken ct)
    {
        var isCorrectionInvoice =
            invoice.OriginalInvoiceHeadId.HasValue ||
            invoice.CorrectionType.HasValue;

        if (!isCorrectionInvoice)
            return null;

        return await _correctionRepository.GetByNewInvoiceHeadIdAsync(
            invoice.Id,
            ct);
    }

    private static void MarkCorrectionIssuing(
        InvoiceCorrectionCase? correctionCase)
    {
        if (correctionCase == null)
            return;

        correctionCase.Status = InvoiceCorrectionStatus.Issuing;
        correctionCase.LastErrorCode = null;
        correctionCase.LastErrorMessage = null;
    }

    private static void MarkCorrectionFailed(
        InvoiceCorrectionCase? correctionCase,
        string? errorCode,
        string? errorMessage)
    {
        if (correctionCase == null)
            return;

        correctionCase.Status = InvoiceCorrectionStatus.Failed;
        correctionCase.LastErrorCode = errorCode;
        correctionCase.LastErrorMessage = errorMessage;
    }

    private static void MarkCorrectionIssued(
        InvoiceCorrectionCase? correctionCase,
        DateTime issuedAtUtc)
    {
        if (correctionCase == null)
            return;

        correctionCase.Status = InvoiceCorrectionStatus.Issued;
        correctionCase.IssuedAtUtc = issuedAtUtc;
        correctionCase.LastErrorCode = null;
        correctionCase.LastErrorMessage = null;
    }

    private async Task MarkCorrectionFailedAsync(
        InvoiceCorrectionCase? correctionCase,
        string? errorCode,
        string? errorMessage,
        CancellationToken ct)
    {
        if (correctionCase == null)
            return;

        MarkCorrectionFailed(
            correctionCase,
            errorCode,
            errorMessage);

        await _invoiceRepository.SaveChangesAsync(ct);
    }

    private static Result<bool> ValidateInvoiceAmountBeforeIssue(InvoiceHead invoice)
    {
        var isCorrectionInvoice =
            invoice.OriginalInvoiceHeadId.HasValue ||
            invoice.CorrectionType.HasValue;

        if (!isCorrectionInvoice && invoice.GrandTotal <= 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.TotalInvalid",
                    "Hóa đơn gốc phải có tổng tiền lớn hơn 0."));
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.Replacement &&
            invoice.GrandTotal <= 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.ReplacementTotalInvalid",
                    "Hóa đơn thay thế phải có tổng tiền lớn hơn 0."));
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentAmount &&
            invoice.GrandTotal == 0)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.AdjustmentAmountTotalInvalid",
                    "Hóa đơn điều chỉnh tiền phải có tổng tiền khác 0."));
        }

        return Result<bool>.Success(true);
    }

    private static Result<bool> ValidateInvoiceDetailsBeforeIssue(InvoiceHead invoice)
    {
        var isAdjustmentInfoInvoice =
            invoice.CorrectionType == InvoiceCorrectionType.AdjustmentInfo;

        var hasValidDetails = invoice.Details
            .Where(x => !x.IsDeleted)
            .Any(x =>
                !string.IsNullOrWhiteSpace(x.ItemName) &&
                (
                    x.Quantity != 0 ||
                    isAdjustmentInfoInvoice
                ));

        if (!hasValidDetails)
        {
            return Result<bool>.Failure(
                Error.Validation(
                    "Invoice.NoDetails",
                    "Hóa đơn chưa có dòng chi tiết hợp lệ để phát hành."));
        }

        return Result<bool>.Success(true);
    }

    private static bool IsAlreadyIssued(InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static bool MustSyncUuidBeforeIssue(InvoiceHead invoice)
    {
        if (invoice.ProviderStatus == InvoiceProviderStatus.Issuing)
            return true;

        if (invoice.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber)
            return true;

        if (invoice.ProviderStatus != InvoiceProviderStatus.IssueFailed)
            return false;

        var code = invoice.LastErrorCode ?? string.Empty;
        var message = invoice.LastErrorMessage ?? string.Empty;

        if (code.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.Equals("HTTP_500", StringComparison.OrdinalIgnoreCase))
            return true;

        if (code.Equals("VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
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