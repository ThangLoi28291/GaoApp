using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelInvoiceUiStateBuilder
{
    public static ViettelInvoiceUiState Build(InvoiceHeadDto invoice)
    {
        var isIssued = IsIssuedLike(invoice);
        var requiresUuidSyncBeforeIssue = IsProcessingOrUnclear(invoice);

        var hasOfficialPdf =
            invoice.OfficialPdfStatus == InvoiceFileDownloadStatus.Downloaded ||
            !string.IsNullOrWhiteSpace(invoice.PdfFilePath);

        var hasOfficialZip =
            invoice.OfficialZipXmlStatus == InvoiceFileDownloadStatus.Downloaded ||
            !string.IsNullOrWhiteSpace(invoice.ZipFilePath);

        var issueBlockReason = GetIssueBlockReason(
            invoice,
            isIssued,
            requiresUuidSyncBeforeIssue);

        var canIssue = CanIssueInvoice(
            invoice,
            isIssued,
            requiresUuidSyncBeforeIssue);

        return new ViettelInvoiceUiState
        {
            ProviderStatus = (int)invoice.ProviderStatus,
            ProviderStatusName = GetProviderStatusName(invoice.ProviderStatus),
            ProviderStatusBadgeClass = GetProviderStatusBadgeClass(invoice.ProviderStatus),

            IsIssued = isIssued,

            HasOfficialPdf = hasOfficialPdf,
            HasOfficialZip = hasOfficialZip,

            CanPreviewDraft =
                !isIssued &&
                invoice.ProviderStatus != InvoiceProviderStatus.Issuing &&
                invoice.ProviderStatus != InvoiceProviderStatus.Cancelled,

            CanIssue = canIssue,

            CanSyncByUuid =
                !string.IsNullOrWhiteSpace(invoice.TransactionUuid),

            CanDownloadOfficialFiles =
                isIssued &&
                invoice.ProviderStatus != InvoiceProviderStatus.Cancelled,

            CanViewSavedPdf = hasOfficialPdf,

            CanDownloadSavedZip = hasOfficialZip,

            RequiresUuidSyncBeforeIssue = requiresUuidSyncBeforeIssue,

            IssueBlockReason = issueBlockReason,

            // Không chặn gửi lại email nếu đã gửi rồi.
            // EmailStatus chỉ để hiển thị trạng thái, còn gửi lại vẫn cho phép khi hóa đơn đã phát hành.
            CanSendEmail =
                isIssued &&
                invoice.ProviderStatus != InvoiceProviderStatus.Cancelled
        };
    }

    private static bool CanIssueInvoice(
        InvoiceHeadDto invoice,
        bool isIssued,
        bool requiresUuidSyncBeforeIssue)
    {
        if (isIssued)
            return false;

        if (requiresUuidSyncBeforeIssue)
            return false;

        if (invoice.ProviderStatus == InvoiceProviderStatus.Issuing)
            return false;

        if (invoice.ProviderStatus == InvoiceProviderStatus.Cancelled)
            return false;

        var hasValidDetails = HasValidDetails(invoice);

        if (!hasValidDetails)
            return false;

        var isCorrectionInvoice = IsCorrectionInvoice(invoice);

        if (!isCorrectionInvoice)
            return invoice.GrandTotal > 0;

        if (invoice.CorrectionType == InvoiceCorrectionType.Replacement)
            return invoice.GrandTotal > 0;

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentAmount)
            return invoice.GrandTotal != 0;

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentInfo)
            return true;

        return false;
    }

    private static bool IsIssuedLike(InvoiceHeadDto invoice)
    {
        if (!string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo))
            return true;

        return invoice.ProviderStatus is
            InvoiceProviderStatus.Issued or
            InvoiceProviderStatus.PdfDownloaded or
            InvoiceProviderStatus.ZipDownloaded or
            InvoiceProviderStatus.EmailSent;
    }

    private static bool IsProcessingOrUnclear(InvoiceHeadDto invoice)
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

    private static string GetIssueBlockReason(
        InvoiceHeadDto invoice,
        bool isIssued,
        bool requiresUuidSyncBeforeIssue)
    {
        if (isIssued)
        {
            return string.IsNullOrWhiteSpace(invoice.ProviderInvoiceNo)
                ? "Hóa đơn đã phát hành Viettel."
                : $"Hóa đơn đã phát hành Viettel. Số hóa đơn: {invoice.ProviderInvoiceNo}.";
        }

        if (invoice.ProviderStatus == InvoiceProviderStatus.Cancelled)
        {
            return "Hóa đơn đã hủy, không thể phát hành.";
        }

        if (requiresUuidSyncBeforeIssue)
        {
            return "Lần phát hành trước chưa rõ kết quả. Cần bấm Tra cứu UUID trước khi phát hành lại.";
        }

        if (!HasValidDetails(invoice))
        {
            return "Hóa đơn chưa có dòng chi tiết.";
        }

        var isCorrectionInvoice = IsCorrectionInvoice(invoice);

        if (!isCorrectionInvoice && invoice.GrandTotal <= 0)
        {
            return "Hóa đơn gốc phải có tổng tiền lớn hơn 0.";
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.Replacement &&
            invoice.GrandTotal <= 0)
        {
            return "Hóa đơn thay thế phải có tổng tiền lớn hơn 0.";
        }

        if (invoice.CorrectionType == InvoiceCorrectionType.AdjustmentAmount &&
            invoice.GrandTotal == 0)
        {
            return "Hóa đơn điều chỉnh tiền phải có tổng tiền khác 0.";
        }

        return string.Empty;
    }

    private static bool HasValidDetails(InvoiceHeadDto invoice)
    {
        return invoice.Details != null &&
               invoice.Details.Any(x =>
                   !string.IsNullOrWhiteSpace(x.ItemName) &&
                   (
                       x.Quantity != 0 ||
                       invoice.CorrectionType == InvoiceCorrectionType.AdjustmentInfo
                   ));
    }

    private static bool IsCorrectionInvoice(InvoiceHeadDto invoice)
    {
        return invoice.OriginalInvoiceHeadId.HasValue ||
               invoice.CorrectionType.HasValue;
    }

    private static string GetProviderStatusName(InvoiceProviderStatus status)
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
            // Sau 21.9, PDF/ZIP/Email đã có status riêng,
            // nên các trạng thái này vẫn xem là đã phát hành để UI không bị lệch.
            InvoiceProviderStatus.PdfDownloaded => "Đã phát hành",
            InvoiceProviderStatus.ZipDownloaded => "Đã phát hành",
            InvoiceProviderStatus.EmailSent => "Đã phát hành",

            _ => status.ToString()
        };
    }

    private static string GetProviderStatusBadgeClass(InvoiceProviderStatus status)
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

internal class ViettelInvoiceUiState
{
    public int ProviderStatus { get; set; }

    public string ProviderStatusName { get; set; } = string.Empty;

    public string ProviderStatusBadgeClass { get; set; } = "bg-secondary";

    public bool IsIssued { get; set; }

    public bool HasOfficialPdf { get; set; }

    public bool HasOfficialZip { get; set; }

    public bool CanPreviewDraft { get; set; }

    public bool CanIssue { get; set; }

    public bool CanSyncByUuid { get; set; }

    public bool CanDownloadOfficialFiles { get; set; }

    public bool CanViewSavedPdf { get; set; }

    public bool CanDownloadSavedZip { get; set; }

    public bool RequiresUuidSyncBeforeIssue { get; set; }

    public string IssueBlockReason { get; set; } = string.Empty;

    public bool CanSendEmail { get; set; }
}