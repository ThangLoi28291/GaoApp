using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceIntegrationLogCleanupRequestDto
{
    public bool DryRun { get; set; } = true;

    public bool IncludeFailedLogs { get; set; } = false;

    public int BuildPayloadSuccessDays { get; set; } = 7;

    public int PreviewDraftSuccessDays { get; set; } = 30;

    public int SendEmailSuccessDays { get; set; } = 90;

    public int SyncInvoiceListSuccessDays { get; set; } = 90;

    public int DownloadFileSuccessDays { get; set; } = 180;

    public int OtherSuccessDays { get; set; } = 180;

    public int FailedLogDays { get; set; } = 365;

    public int MaxRowsPerRun { get; set; } = 5000;
}

public class InvoiceIntegrationLogCleanupResultDto
{
    public bool DryRun { get; set; }

    public DateTime NowUtc { get; set; }

    public int TotalCandidates { get; set; }

    public int DeletedCount { get; set; }

    public int MaxRowsPerRun { get; set; }

    public List<InvoiceIntegrationLogCleanupPolicyRowDto> Rows { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}

public class InvoiceIntegrationLogCleanupPolicyRowDto
{
    public string PolicyName { get; set; } = string.Empty;

    public string ActionNames { get; set; } = string.Empty;

    public bool IsSuccess { get; set; }

    public int RetentionDays { get; set; }

    public DateTime DeleteBeforeUtc { get; set; }

    public int CandidateCount { get; set; }

    public int DeletedCount { get; set; }
}