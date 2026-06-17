using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class ViettelInvoiceIssueService : IViettelInvoiceIssueService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceProviderSettingRepository _settingRepository;
    private readonly IViettelInvoicePayloadBuilder _payloadBuilder;
    private readonly IViettelInvoiceIssueClient _issueClient;

    public ViettelInvoiceIssueService(
        IInvoiceRepository invoiceRepository,
        IInvoiceProviderSettingRepository settingRepository,
        IViettelInvoicePayloadBuilder payloadBuilder,
        IViettelInvoiceIssueClient issueClient)
    {
        _invoiceRepository = invoiceRepository;
        _settingRepository = settingRepository;
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
                Error.Validation("Invoice.InvalidInvoiceHeadId", "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadWithDetailsByIdAsync(invoiceHeadId, ct);

        if (invoice == null)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

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

        if (invoice.GrandTotal <= 0)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Invoice.TotalInvalid", "Không được phát hành hóa đơn có tổng tiền bằng 0."));
        }

        if (!invoice.Details.Any(x => !x.IsDeleted && x.Quantity > 0))
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("Invoice.NoDetails", "Hóa đơn chưa có dòng chi tiết."));
        }

        if (string.IsNullOrWhiteSpace(invoice.TransactionUuid))
        {
            invoice.TransactionUuid = Guid.NewGuid().ToString();
        }

        var setting = await _settingRepository.GetActiveViettelAsync(ct);

        if (setting == null)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation("InvoiceProvider.NotConfigured", "Chưa có cấu hình Viettel đang dùng."));
        }

        var payloadResult = await _payloadBuilder.BuildAsync(invoiceHeadId, ct);

        if (!payloadResult.IsSuccess)
        {
            return Result<ViettelInvoiceIssueResultDto>.Failure(payloadResult.Error!);
        }

        invoice.ProviderStatus = InvoiceProviderStatus.Issuing;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;
        invoice.LastSyncedAtUtc = DateTime.UtcNow;

        await _invoiceRepository.SaveChangesAsync(ct);

        var issueResult = await _issueClient.IssueInvoiceAsync(
            invoiceHeadId: invoiceHeadId,
            baseUrl: setting.BaseUrl,
            username: setting.Username,
            password: setting.Password,
            authMode: setting.AuthMode,
            supplierTaxCode: setting.SupplierTaxCode,
            payload: payloadResult.Value.Payload,
            ct: ct);

        if (!issueResult.IsSuccess)
        {
            invoice.ProviderStatus = InvoiceProviderStatus.IssueFailed;
            invoice.LastErrorCode = issueResult.Error?.Code;
            invoice.LastErrorMessage = issueResult.Error?.Message;
            invoice.LastSyncedAtUtc = DateTime.UtcNow;

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

            await _invoiceRepository.SaveChangesAsync(ct);

            return Result<ViettelInvoiceIssueResultDto>.Failure(
                Error.Validation(
                    issue.ErrorCode ?? "Viettel.IssueFailed",
                    issue.ErrorMessage ?? "Viettel phát hành hóa đơn thất bại."));
        }

        invoice.ProviderStatus = string.IsNullOrWhiteSpace(issue.InvoiceNo)
            ? InvoiceProviderStatus.IssuedWaitingNumber
            : InvoiceProviderStatus.Issued;

        invoice.ProviderInvoiceNo = issue.InvoiceNo;
        invoice.ProviderTransactionId = issue.TransactionId;
        invoice.ReservationCode = issue.ReservationCode;
        invoice.CodeOfTax = issue.CodeOfTax;

        if (!string.IsNullOrWhiteSpace(issue.InvoiceNo))
        {
            invoice.InvoiceNumber = issue.InvoiceNo;
        }

        invoice.IssuedAtUtc = DateTime.UtcNow;
        invoice.LastSyncedAtUtc = DateTime.UtcNow;
        invoice.LastErrorCode = null;
        invoice.LastErrorMessage = null;

        invoice.IsLocked = true;
        invoice.LockedAtUtc ??= DateTime.UtcNow;
        invoice.LockReason = string.IsNullOrWhiteSpace(issue.InvoiceNo)
            ? "Đã gửi phát hành Viettel, đang chờ số hóa đơn."
            : $"Đã phát hành Viettel. Số hóa đơn: {issue.InvoiceNo}.";

        await _invoiceRepository.SaveChangesAsync(ct);

        return Result<ViettelInvoiceIssueResultDto>.Success(issue);
    }
    private static bool IsAlreadyIssued(GaoApp.Domain.Entities.InvoiceHead invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static bool MustSyncUuidBeforeIssue(GaoApp.Domain.Entities.InvoiceHead invoice)
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
}