namespace GaoApp.Application.Common.Options;

public sealed class ProfitReportLimits
{
    public const string SectionName = "Reports:Profit";
    public int MaxConcurrent { get; set; } = 2;
    public int MaxQueued { get; set; } = 48;
    public int QueueTimeoutMilliseconds { get; set; } = 5000;
    public int ExecutionTimeoutSeconds { get; set; } = 30;
    public int MaxSourceRows { get; set; } = 100000;

    public bool IsValid() => MaxConcurrent is >= 1 and <= 8 && MaxQueued is >= 0 and <= 50 &&
        QueueTimeoutMilliseconds is >= 100 and <= 10000 && ExecutionTimeoutSeconds is >= 1 and <= 120 &&
        MaxSourceRows is >= 100 and <= 500000;
}
