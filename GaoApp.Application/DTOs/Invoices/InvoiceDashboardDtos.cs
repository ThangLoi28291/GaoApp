namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceDashboardQueryDto
{
    public DateTime FromDate { get; set; } = DateTime.Today;

    public DateTime ToDate { get; set; } = DateTime.Today;
}

public class InvoiceDashboardDto
{
    public DateTime FromDate { get; set; }

    public DateTime ToDate { get; set; }

    public int TotalInvoices { get; set; }

    public int LocalDraftCount { get; set; }

    public int ReadyToIssueCount { get; set; }

    public int IssuedCount { get; set; }

    public int IssueFailedCount { get; set; }

    public int NeedUuidSyncCount { get; set; }

    public int PdfDownloadedCount { get; set; }

    public int ZipDownloadedCount { get; set; }

    public int EmailSentCount { get; set; }

    public int MissingPdfCount { get; set; }

    public int MissingZipCount { get; set; }

    public decimal IssuedAmount { get; set; }

    public int ErrorLogCount { get; set; }

    public int TimeoutLogCount { get; set; }

    public int Http500LogCount { get; set; }

    public List<InvoiceDashboardStatusRowDto> StatusRows { get; set; } = new();

    public List<InvoiceDashboardRecentErrorDto> RecentErrors { get; set; } = new();
}

public class InvoiceDashboardStatusRowDto
{
    public string StatusName { get; set; } = string.Empty;

    public int Count { get; set; }

    public decimal Amount { get; set; }
}

public class InvoiceDashboardRecentErrorDto
{
    public int LogId { get; set; }

    public int InvoiceHeadId { get; set; }

    public string ActionName { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime StartedAtUtc { get; set; }
}