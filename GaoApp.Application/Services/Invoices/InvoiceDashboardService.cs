using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceDashboardService : IInvoiceDashboardService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceIntegrationLogRepository _logRepository;

    public InvoiceDashboardService(
        IInvoiceRepository invoiceRepository,
        IInvoiceIntegrationLogRepository logRepository)
    {
        _invoiceRepository = invoiceRepository;
        _logRepository = logRepository;
    }

    public async Task<Result<InvoiceDashboardDto>> GetDashboardAsync(
        InvoiceDashboardQueryDto query,
        CancellationToken ct = default)
    {
        var fromDate = query.FromDate.Date;
        var toDate = query.ToDate.Date;

        if (toDate < fromDate)
        {
            return Result<InvoiceDashboardDto>.Failure(
                Error.Validation("InvoiceDashboard.DateInvalid", "Đến ngày phải lớn hơn hoặc bằng từ ngày."));
        }

        var invoices = await _invoiceRepository.GetInvoiceHeadsForDashboardAsync(
            fromDate,
            toDate,
            ct);

        var logs = await _logRepository.GetLogsForDashboardAsync(
            fromDate,
            toDate,
            ct);

        var issuedLike = invoices
            .Where(x => IsIssuedLike(x.ProviderStatus, x.ProviderInvoiceNo))
            .ToList();

        var result = new InvoiceDashboardDto
        {
            FromDate = fromDate,
            ToDate = toDate,

            TotalInvoices = invoices.Count,
            LocalDraftCount = invoices.Count(x => x.ProviderStatus == InvoiceProviderStatus.LocalDraft),
            ReadyToIssueCount = invoices.Count(x => x.ProviderStatus == InvoiceProviderStatus.ReadyToIssue),
            IssuedCount = issuedLike.Count,
            IssueFailedCount = invoices.Count(x => x.ProviderStatus == InvoiceProviderStatus.IssueFailed),
            NeedUuidSyncCount = invoices.Count(NeedUuidSync),
            PdfDownloadedCount = invoices.Count(x => !string.IsNullOrWhiteSpace(x.PdfFilePath)),
            ZipDownloadedCount = invoices.Count(x => !string.IsNullOrWhiteSpace(x.ZipFilePath)),
            EmailSentCount = invoices.Count(x => x.ProviderStatus == InvoiceProviderStatus.EmailSent),
            MissingPdfCount = issuedLike.Count(x => string.IsNullOrWhiteSpace(x.PdfFilePath)),
            MissingZipCount = issuedLike.Count(x => string.IsNullOrWhiteSpace(x.ZipFilePath)),
            IssuedAmount = issuedLike.Sum(x => x.GrandTotal),

            ErrorLogCount = logs.Count(x => !x.IsSuccess),
            TimeoutLogCount = logs.Count(x =>
                string.Equals(x.ErrorCode, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                (x.ErrorMessage ?? "").Contains("timeout", StringComparison.OrdinalIgnoreCase)),
            Http500LogCount = logs.Count(x =>
                (x.ErrorCode ?? "").Contains("HTTP_500", StringComparison.OrdinalIgnoreCase) ||
                (x.ErrorMessage ?? "").Contains("HTTP 500", StringComparison.OrdinalIgnoreCase))
        };

        result.StatusRows = invoices
            .GroupBy(x => x.ProviderStatus)
            .Select(g => new InvoiceDashboardStatusRowDto
            {
                StatusName = GetStatusName(g.Key),
                Count = g.Count(),
                Amount = g.Sum(x => x.GrandTotal)
            })
            .OrderByDescending(x => x.Count)
            .ToList();

        result.RecentErrors = logs
            .Where(x => !x.IsSuccess)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(10)
            .Select(x => new InvoiceDashboardRecentErrorDto
            {
                LogId = x.Id,
                InvoiceHeadId = x.InvoiceHeadId,
                ActionName = GetActionName(x.ActionType),
                ErrorCode = x.ErrorCode,
                ErrorMessage = x.ErrorMessage,
                StartedAtUtc = x.StartedAtUtc
            })
            .ToList();

        return Result<InvoiceDashboardDto>.Success(result);
    }

    private static bool IsIssuedLike(
        InvoiceProviderStatus status,
        string? providerInvoiceNo)
    {
        if (!string.IsNullOrWhiteSpace(providerInvoiceNo))
            return true;

        return status is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static bool NeedUuidSync(InvoiceHead invoice)
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

        if (message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            return true;

        if (message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static string GetStatusName(InvoiceProviderStatus status)
    {
        return status switch
        {
            InvoiceProviderStatus.LocalDraft => "Chưa phát hành",
            InvoiceProviderStatus.ReadyToIssue => "Sẵn sàng phát hành",
            InvoiceProviderStatus.Previewed => "Đã preview nháp",
            InvoiceProviderStatus.DraftSent => "Đã gửi nháp",
            InvoiceProviderStatus.Issuing => "Đang phát hành",
            InvoiceProviderStatus.Issued => "Đã phát hành",
            InvoiceProviderStatus.IssuedWaitingNumber => "Chờ tra cứu số HĐ",
            InvoiceProviderStatus.IssueFailed => "Phát hành lỗi",
            InvoiceProviderStatus.Cancelled => "Đã hủy",
            InvoiceProviderStatus.PdfDownloaded => "Đã tải PDF",
            InvoiceProviderStatus.ZipDownloaded => "Đã tải ZIP/XML",
            InvoiceProviderStatus.EmailSent => "Đã gửi email",
            _ => status.ToString()
        };
    }

    private static string GetActionName(InvoiceIntegrationActionType actionType)
    {
        return actionType switch
        {
            InvoiceIntegrationActionType.Login => "Đăng nhập",
            InvoiceIntegrationActionType.BuildPayload => "Build JSON",
            InvoiceIntegrationActionType.PreviewDraft => "Preview PDF nháp",
            InvoiceIntegrationActionType.CreateDraft => "Tạo nháp",
            InvoiceIntegrationActionType.IssueInvoice => "Phát hành Viettel",
            InvoiceIntegrationActionType.SearchByTransactionUuid => "Tra cứu UUID",
            InvoiceIntegrationActionType.DownloadPdf => "Tải PDF chính thức",
            InvoiceIntegrationActionType.DownloadZip => "Tải ZIP/XML",
            InvoiceIntegrationActionType.SendEmail => "Gửi email",
            InvoiceIntegrationActionType.UpdatePaymentStatus => "Cập nhật thanh toán",
            InvoiceIntegrationActionType.CancelPaymentStatus => "Hủy thanh toán",
            InvoiceIntegrationActionType.UpdatePrintStatus => "Cập nhật trạng thái in",
            InvoiceIntegrationActionType.CancelInvoice => "Hủy hóa đơn",
            InvoiceIntegrationActionType.SyncInvoiceList => "Đồng bộ danh sách",
            _ => actionType.ToString()
        };
    }
}