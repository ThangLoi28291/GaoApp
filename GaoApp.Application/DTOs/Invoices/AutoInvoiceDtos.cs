using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

public sealed class AutoInvoiceSettingsDto
{
    public int StoreId { get; set; }
    public bool IsEnabled { get; set; }
    public int MinimumAgeMinutes { get; set; }
    public decimal SeparateAmountThreshold { get; set; }
    public decimal GroupTargetAmount { get; set; }
    public int SendIntervalSeconds { get; set; }
    public TimeSpan ClosingTimeLocal { get; set; }
    public bool IssueOldDayRemainder { get; set; }
    public AutoInvoiceScopeMode ScopeMode { get; set; }
    public DateTime? ScopeStartDateLocal { get; set; }
    public DateTime? ScopeEndDateLocal { get; set; }
    public string TimeZoneId { get; set; } = string.Empty;
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
}

public sealed class UpdateAutoInvoiceSettingsRequest
{
    public bool IsEnabled { get; set; }
    public int MinimumAgeMinutes { get; set; }
    public decimal SeparateAmountThreshold { get; set; }
    public decimal GroupTargetAmount { get; set; }
    public int SendIntervalSeconds { get; set; }
    public TimeSpan ClosingTimeLocal { get; set; }
    public bool IssueOldDayRemainder { get; set; }
    public AutoInvoiceScopeMode ScopeMode { get; set; }
    public DateTime? ScopeStartDateLocal { get; set; }
    public DateTime? ScopeEndDateLocal { get; set; }
    public string? TimeZoneId { get; set; }
}

public sealed class AutoInvoiceDashboardQueryDto
{
    public AutoInvoiceScopeMode ScopeMode { get; set; } = AutoInvoiceScopeMode.Today;
    public DateTime? StartDateLocal { get; set; }
    public DateTime? EndDateLocal { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public sealed class AutoInvoiceDashboardDto
{
    public AutoInvoiceSettingsDto Settings { get; set; } = new();
    public AutoInvoiceWorkerDto Worker { get; set; } = new();
    public AutoInvoiceDashboardSummaryDto Summary { get; set; } = new();
    public List<AutoInvoiceQueueItemDto> Queue { get; set; } = new();
    public List<AutoInvoiceGroupDto> Groups { get; set; } = new();
    public List<AutoInvoiceErrorDto> Errors { get; set; } = new();
    public List<AutoInvoiceHistoryDto> History { get; set; } = new();
}

public sealed class AutoInvoiceDashboardSummaryDto
{
    public int TodayCount { get; set; }
    public int PendingCount { get; set; }
    public int GroupWaitingCount { get; set; }
    public int ErrorCount { get; set; }
    public int UnknownCount { get; set; }
    public int IssuedCount { get; set; }
}

public sealed class AutoInvoiceWorkerDto
{
    public bool IsRunning { get; set; }
    public string WorkerName { get; set; } = string.Empty;
    public string? WorkerInstanceId { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? LastHeartbeatAtUtc { get; set; }
    public DateTime? LastScanAtUtc { get; set; }
    public int? CurrentOperationId { get; set; }
    public int? CurrentInvoiceHeadId { get; set; }
    public DateTime? NextRunAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
    public string? LastResult { get; set; }
}

public sealed class AutoInvoiceQueueItemDto
{
    public int InvoiceHeadId { get; set; }
    public int? OrderId { get; set; }
    public string? OrderNumber { get; set; }
    public DateTime SaleAtUtc { get; set; }
    public DateTime SaleDateLocal { get; set; }
    public string BuyerType { get; set; } = string.Empty;
    public string? BuyerDisplay { get; set; }
    public decimal GrandTotal { get; set; }
    public InvoiceProviderStatus ProviderStatus { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string? StoreName { get; set; }
    public int? LegalEntityId { get; set; }
    public int? InvoiceProviderSettingId { get; set; }
    public string? ProviderCode { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsGroupedConsumer { get; set; }
}

public sealed class AutoInvoiceGroupDto
{
    public string GroupKey { get; set; } = string.Empty;
    public DateTime SaleDateLocal { get; set; }
    public int? LegalEntityId { get; set; }
    public int? InvoiceProviderSettingId { get; set; }
    public decimal TotalAmount { get; set; }
    public bool IsReadyByTarget { get; set; }
    public bool IsReadyByClosing { get; set; }
    public List<int> InvoiceHeadIds { get; set; } = new();
}

public sealed class AutoInvoiceErrorDto
{
    public int InvoiceHeadId { get; set; }
    public int? OperationId { get; set; }
    public string? OrderNumber { get; set; }
    public string ErrorCode { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public bool RequiresUuidLookup { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}

public sealed class AutoInvoiceHistoryDto
{
    public int OperationId { get; set; }
    public AutoInvoiceOperationKind Kind { get; set; }
    public AutoInvoiceOperationStatus Status { get; set; }
    public DateTime SaleDateLocal { get; set; }
    public int SourceCount { get; set; }
    public int? InvoiceHeadId { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public bool IsManual { get; set; }
    public string? RequestedByUserName { get; set; }
}
