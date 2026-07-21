namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityCanaryStatusDto
{
    public bool IsEnabled { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public string HealthStatus { get; set; } = "Stopped";
    public string HealthMessage { get; set; } = string.Empty;
    public bool IsConfigurationReady { get; set; }
    public bool CanActivate { get; set; }
    public DateTime EvaluatedAtUtc { get; set; }
    public DateTime? MonitoringSinceUtc { get; set; }
    public LegalEntityCanaryOperationalMetricsDto Metrics { get; set; } = new();
    public List<LegalEntityCanaryHealthCheckDto> HealthChecks { get; set; } = [];
    public List<LegalEntityCanaryEventDto> RecentEvents { get; set; } = [];
}

public sealed class LegalEntityCanaryOperationalMetricsDto
{
    public int AllocatedOrderCount { get; set; }
    public int SplitOrderCount { get; set; }
    public int AllocationMismatchCount { get; set; }
    public int InvoiceMismatchCount { get; set; }
    public int OpenInventoryIssueCount { get; set; }
    public int FailedInvoiceCount { get; set; }
    public int StaleReservationCount { get; set; }
    public int PendingMultiModeOrderCount { get; set; }
    public int PendingLegacyModeOrderCount { get; set; }
}

public sealed class LegalEntityCanaryHealthCheckDto
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Level { get; set; } = "Info";
    public bool IsPassed { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class LegalEntityCanaryEventDto
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public bool PreviousIsEnabled { get; set; }
    public bool NewIsEnabled { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ActivationAtUtc { get; set; }
    public int? ChangedByUserId { get; set; }
    public string? ChangedByUserName { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool PreflightPassed { get; set; }
}

public sealed class LegalEntityCanaryStateChangeDto
{
    public bool IsEnabled { get; set; }
    public bool WasChanged { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public int? ActivationEventId { get; set; }
    public string Message { get; set; } = string.Empty;
}
