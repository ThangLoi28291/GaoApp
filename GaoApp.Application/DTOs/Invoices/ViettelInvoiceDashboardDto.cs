using GaoApp.Application.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public enum ViettelInvoiceDashboardFilter
{
    All = 0,

    IssuedMissingPdf = 1,

    IssuedMissingZipXml = 2,

    IssuedMissingEmail = 3,

    PdfFailed = 4,

    ZipXmlFailed = 5,

    EmailFailed = 6,

    UuidNeedSync = 7,

    IssueFailed = 8,

    IssuedComplete = 9
}

public class ViettelInvoiceDashboardQueryDto
{
    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public string? Keyword { get; set; }

    public ViettelInvoiceDashboardFilter Filter { get; set; } =
        ViettelInvoiceDashboardFilter.All;

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public class ViettelInvoiceDashboardDto
{
    public ViettelInvoiceDashboardQueryDto Query { get; set; } = new();

    public ViettelInvoiceDashboardSummaryDto Summary { get; set; } = new();

    public PagedResult<ViettelInvoiceDashboardItemDto> Items { get; set; } = new();
}

public class ViettelInvoiceDashboardSummaryDto
{
    public int TotalInvoices { get; set; }

    public int IssuedCount { get; set; }

    public int MissingPdfCount { get; set; }

    public int MissingZipXmlCount { get; set; }

    public int MissingEmailCount { get; set; }

    public int PdfFailedCount { get; set; }

    public int ZipXmlFailedCount { get; set; }

    public int EmailFailedCount { get; set; }

    public int UuidNeedSyncCount { get; set; }

    public int IssueFailedCount { get; set; }

    public int CompleteCount { get; set; }
}

public class ViettelInvoiceDashboardItemDto
{
    public int Id { get; set; }

    public int? OrderId { get; set; }

    public string? OrderNumber { get; set; }

    public DateTime InvoiceDate { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? ProviderInvoiceNo { get; set; }

    public string? TransactionUuid { get; set; }

    public InvoiceProviderStatus ProviderStatus { get; set; }

    public string ProviderStatusName { get; set; } = string.Empty;

    public string ProviderStatusBadgeClass { get; set; } = "bg-secondary";

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? BuyerEmail { get; set; }

    public decimal GrandTotal { get; set; }

    public DateTime? IssuedAtUtc { get; set; }

    public DateTime? LastSyncedAtUtc { get; set; }

    public string? LastErrorCode { get; set; }

    public string? LastErrorMessage { get; set; }

    public string? PdfFilePath { get; set; }

    public string? ZipFilePath { get; set; }

    public InvoiceFileDownloadStatus OfficialPdfStatus { get; set; }

    public DateTime? OfficialPdfDownloadedAtUtc { get; set; }

    public string? OfficialPdfFileName { get; set; }

    public InvoiceFileDownloadStatus OfficialZipXmlStatus { get; set; }

    public DateTime? OfficialZipXmlDownloadedAtUtc { get; set; }

    public string? OfficialZipXmlFileName { get; set; }

    public InvoiceEmailSendStatus EmailStatus { get; set; }

    public DateTime? EmailSentAtUtc { get; set; }

    public string? LastEmailTo { get; set; }

    public int EmailSendCount { get; set; }

    public string? LastEmailErrorMessage { get; set; }

    public bool IsIssued { get; set; }

    public bool HasOfficialPdf { get; set; }

    public bool HasOfficialZipXml { get; set; }

    public bool HasEmailSent { get; set; }

    public bool CanSyncByUuid { get; set; }

    public bool CanDownloadOfficialFiles { get; set; }

    public bool CanSendEmail { get; set; }

    public string ProblemText { get; set; } = string.Empty;

    public string ProblemBadgeClass { get; set; } = "bg-secondary";
}