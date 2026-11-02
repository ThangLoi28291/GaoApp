using GaoApp.Application.Common.Security;
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
        query ??= new InvoiceDashboardQueryDto();
        query.NormalizeAndValidate();
        var fromDate = query.FromDate;
        var toDate = query.ToDate;
        var rows = await _invoiceRepository.GetInvoiceDashboardAggregatesAsync(fromDate, toDate, ct);

        var logs = await _logRepository.GetLogsForDashboardAsync(
            fromDate,
            toDate,
            ct);

        var result = new InvoiceDashboardDto
        {
            FromDate = fromDate, ToDate = toDate,
            TotalInvoices = rows.Sum(x => x.Count),
            LocalDraftCount = rows.Where(x => x.ProviderStatus == InvoiceProviderStatus.LocalDraft).Sum(x => x.Count),
            ReadyToIssueCount = rows.Where(x => x.ProviderStatus == InvoiceProviderStatus.ReadyToIssue).Sum(x => x.Count),
            IssuedCount = rows.Sum(x => x.IssuedCount),
            IssueFailedCount = rows.Where(x => x.ProviderStatus == InvoiceProviderStatus.IssueFailed).Sum(x => x.Count),
            NeedUuidSyncCount = rows.Sum(x => x.NeedUuidSyncCount),
            PdfDownloadedCount = rows.Sum(x => x.PdfDownloadedCount),
            ZipDownloadedCount = rows.Sum(x => x.ZipDownloadedCount),
            EmailSentCount = rows.Where(x => x.ProviderStatus == InvoiceProviderStatus.EmailSent).Sum(x => x.Count),
            MissingPdfCount = rows.Sum(x => x.MissingPdfCount),
            MissingZipCount = rows.Sum(x => x.MissingZipCount),
            IssuedAmount = rows.Sum(x => x.IssuedAmount),

            ErrorLogCount = logs.Count(x => !x.IsSuccess),
            TimeoutLogCount = logs.Count(x =>
                string.Equals(x.ErrorCode, "TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
                (x.ErrorMessage ?? "").Contains("timeout", StringComparison.OrdinalIgnoreCase)),
            Http500LogCount = logs.Count(x =>
                (x.ErrorCode ?? "").Contains("HTTP_500", StringComparison.OrdinalIgnoreCase) ||
                (x.ErrorMessage ?? "").Contains("HTTP 500", StringComparison.OrdinalIgnoreCase))
        };

        result.StatusRows = rows.Select(x => new InvoiceDashboardStatusRowDto
        {
            StatusName = GetStatusName(x.ProviderStatus), Count = x.Count, Amount = x.Amount
        }).OrderByDescending(x => x.Count).ToList();

        result.RecentErrors = logs
            .Where(x => !x.IsSuccess)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(10)
            .Select(x => new InvoiceDashboardRecentErrorDto
            {
                LogId = x.Id,
                InvoiceHeadId = x.InvoiceHeadId,
                ActionName = GetActionName(x.ActionType),
                ErrorCode = InvoiceErrorReadSanitizer.Code(x.ErrorCode),
                ErrorMessage = InvoiceErrorReadSanitizer.Message(x.ErrorMessage, x.ErrorCode),
                StartedAtUtc = x.StartedAtUtc
            })
            .ToList();

        return Result<InvoiceDashboardDto>.Success(result);
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