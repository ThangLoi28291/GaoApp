using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceViettelDashboardService : IInvoiceViettelDashboardService
{
    private readonly IInvoiceRepository _invoiceRepository;

    public InvoiceViettelDashboardService(
        IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task<Result<ViettelInvoiceDashboardDto>> GetDashboardAsync(
        ViettelInvoiceDashboardQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new ViettelInvoiceDashboardQueryDto();

        query.Page = query.Page <= 0 ? 1 : query.Page;
        query.PageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        if (query.PageSize > 100)
            query.PageSize = 100;

        query.Keyword = string.IsNullOrWhiteSpace(query.Keyword)
            ? null
            : query.Keyword.Trim();

        var (summary, items, total) = await _invoiceRepository
            .QueryViettelDashboardAsync(query, ct);

        var dtoItems = items
            .Select(MapToItem)
            .ToList();

        var result = new ViettelInvoiceDashboardDto
        {
            Query = query,
            Summary = summary,
            Items = new PagedResult<ViettelInvoiceDashboardItemDto>
            {
                Items = dtoItems,
                Page = query.Page,
                PageSize = query.PageSize,
                TotalItems = total
            }
        };

        return Result<ViettelInvoiceDashboardDto>.Success(result);
    }

    private static ViettelInvoiceDashboardItemDto MapToItem(
        InvoiceHead entity)
    {
        var isIssued = IsIssuedLike(entity);

        var hasOfficialPdf =
            entity.OfficialPdfStatus == InvoiceFileDownloadStatus.Downloaded ||
            !string.IsNullOrWhiteSpace(entity.PdfFilePath);

        var hasOfficialZipXml =
            entity.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Downloaded ||
            !string.IsNullOrWhiteSpace(entity.ZipFilePath);

        var hasEmailSent =
            entity.EmailStatus == InvoiceEmailSendStatus.Sent;

        var buyerName = !string.IsNullOrWhiteSpace(entity.BuyerLegalName)
            ? entity.BuyerLegalName
            : entity.BuyerName;

        var item = new ViettelInvoiceDashboardItemDto
        {
            Id = entity.Id,
            OrderId = entity.OrderId,
            OrderNumber = entity.Order?.OrderNumber,

            InvoiceDate = entity.InvoiceDate,
            InvoiceNumber = entity.InvoiceNumber,
            ProviderInvoiceNo = entity.ProviderInvoiceNo,
            TransactionUuid = entity.TransactionUuid,

            ProviderStatus = entity.ProviderStatus,
            ProviderStatusName = GetProviderStatusName(entity.ProviderStatus),
            ProviderStatusBadgeClass = GetProviderStatusBadgeClass(entity.ProviderStatus),

            BuyerName = buyerName,
            BuyerTaxCode = entity.BuyerTaxCode,
            BuyerEmail = entity.BuyerEmail,

            GrandTotal = entity.GrandTotal,

            IssuedAtUtc = entity.IssuedAtUtc,
            LastSyncedAtUtc = entity.LastSyncedAtUtc,
            LastErrorCode = entity.LastErrorCode,
            LastErrorMessage = entity.LastErrorMessage,

            PdfFilePath = entity.PdfFilePath,
            ZipFilePath = entity.ZipFilePath,

            OfficialPdfStatus = entity.OfficialPdfStatus,
            OfficialPdfDownloadedAtUtc = entity.OfficialPdfDownloadedAtUtc,
            OfficialPdfFileName = entity.OfficialPdfFileName,

            OfficialZipXmlStatus = entity.OfficialZipXmlStatus,
            OfficialZipXmlDownloadedAtUtc = entity.OfficialZipXmlDownloadedAtUtc,
            OfficialZipXmlFileName = entity.OfficialZipXmlFileName,

            EmailStatus = entity.EmailStatus,
            EmailSentAtUtc = entity.EmailSentAtUtc,
            LastEmailTo = entity.LastEmailTo,
            EmailSendCount = entity.EmailSendCount,
            LastEmailErrorMessage = entity.LastEmailErrorMessage,

            IsIssued = isIssued,
            HasOfficialPdf = hasOfficialPdf,
            HasOfficialZipXml = hasOfficialZipXml,
            HasEmailSent = hasEmailSent,

            CanSyncByUuid =
                !string.IsNullOrWhiteSpace(entity.TransactionUuid),

            CanDownloadOfficialFiles =
                isIssued &&
                entity.ProviderStatus != InvoiceProviderStatus.Cancelled,

            CanSendEmail =
                isIssued &&
                entity.ProviderStatus != InvoiceProviderStatus.Cancelled &&
                !string.IsNullOrWhiteSpace(entity.BuyerEmail)
        };

        ApplyProblemInfo(item);

        return item;
    }

    private static void ApplyProblemInfo(
        ViettelInvoiceDashboardItemDto item)
    {
        if (NeedsUuidSync(item))
        {
            item.ProblemText = "Cần tra cứu UUID";
            item.ProblemBadgeClass = "bg-warning text-dark";
            return;
        }

        if (item.ProviderStatus == InvoiceProviderStatus.IssueFailed)
        {
            item.ProblemText = "Phát hành lỗi";
            item.ProblemBadgeClass = "bg-danger";
            return;
        }

        if (item.OfficialPdfStatus == InvoiceFileDownloadStatus.Failed)
        {
            item.ProblemText = "Tải PDF lỗi";
            item.ProblemBadgeClass = "bg-danger";
            return;
        }

        if (item.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Failed)
        {
            item.ProblemText = "Tải ZIP/XML lỗi";
            item.ProblemBadgeClass = "bg-danger";
            return;
        }

        if (item.EmailStatus == InvoiceEmailSendStatus.Failed)
        {
            item.ProblemText = "Gửi email lỗi";
            item.ProblemBadgeClass = "bg-danger";
            return;
        }

        if (item.IsIssued && !item.HasOfficialPdf)
        {
            item.ProblemText = "Thiếu PDF";
            item.ProblemBadgeClass = "bg-warning text-dark";
            return;
        }

        if (item.IsIssued && !item.HasOfficialZipXml)
        {
            item.ProblemText = "Thiếu ZIP/XML";
            item.ProblemBadgeClass = "bg-warning text-dark";
            return;
        }

        if (item.IsIssued && !item.HasEmailSent)
        {
            item.ProblemText = "Chưa gửi email";
            item.ProblemBadgeClass = "bg-info";
            return;
        }

        if (item.IsIssued && item.HasOfficialPdf && item.HasOfficialZipXml && item.HasEmailSent)
        {
            item.ProblemText = "Đủ file/email";
            item.ProblemBadgeClass = "bg-success";
            return;
        }

        item.ProblemText = "Theo dõi";
        item.ProblemBadgeClass = "bg-secondary";
    }

    private static bool NeedsUuidSync(
        ViettelInvoiceDashboardItemDto item)
    {
        if (item.ProviderStatus == InvoiceProviderStatus.Issuing)
            return true;

        if (item.ProviderStatus == InvoiceProviderStatus.IssuedWaitingNumber)
            return true;

        if (item.ProviderStatus != InvoiceProviderStatus.IssueFailed)
            return false;

        var code = item.LastErrorCode ?? string.Empty;
        var message = item.LastErrorMessage ?? string.Empty;

        return code.Equals("TIMEOUT", StringComparison.OrdinalIgnoreCase) ||
               code.StartsWith("HTTP_5", StringComparison.OrdinalIgnoreCase) ||
               code.Equals("HTTP_500", StringComparison.OrdinalIgnoreCase) ||
               code.Equals("VIETTEL_SERVER_500", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("HTTP 500", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIssuedLike(
        InvoiceHead entity)
    {
        if (!string.IsNullOrWhiteSpace(entity.ProviderInvoiceNo))
            return true;

        return entity.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static string GetProviderStatusName(
        InvoiceProviderStatus status)
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

            // Dữ liệu cũ trước Phase 21.9.
            InvoiceProviderStatus.PdfDownloaded => "Đã phát hành",
            InvoiceProviderStatus.ZipDownloaded => "Đã phát hành",
            InvoiceProviderStatus.EmailSent => "Đã phát hành",

            _ => status.ToString()
        };
    }

    private static string GetProviderStatusBadgeClass(
        InvoiceProviderStatus status)
    {
        return status switch
        {
            InvoiceProviderStatus.LocalDraft => "bg-secondary",
            InvoiceProviderStatus.ReadyToIssue => "bg-info",
            InvoiceProviderStatus.Previewed => "bg-info",
            InvoiceProviderStatus.DraftSent => "bg-info",
            InvoiceProviderStatus.Issuing => "bg-warning text-dark",
            InvoiceProviderStatus.Issued => "bg-success",
            InvoiceProviderStatus.IssuedWaitingNumber => "bg-warning text-dark",
            InvoiceProviderStatus.IssueFailed => "bg-danger",
            InvoiceProviderStatus.Cancelled => "bg-dark",

            // Dữ liệu cũ trước Phase 21.9.
            InvoiceProviderStatus.PdfDownloaded => "bg-success",
            InvoiceProviderStatus.ZipDownloaded => "bg-success",
            InvoiceProviderStatus.EmailSent => "bg-success",

            _ => "bg-secondary"
        };
    }
}