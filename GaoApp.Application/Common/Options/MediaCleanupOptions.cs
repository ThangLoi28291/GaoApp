using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.Common.Options;

public sealed class MediaCleanupOptions
{
    public const string SectionName = "MediaCleanup";
    public bool Enabled { get; set; } = true;
    [Range(1, 168)] public int TempLifetimeHours { get; set; } = 6;
    [Range(1, 365)] public int UnusedRetentionDays { get; set; } = 7;
    [Range(1, 1440)] public int IntervalMinutes { get; set; } = 60;
    [Range(1, 500)] public int BatchSize { get; set; } = 100;
}
