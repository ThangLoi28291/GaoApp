namespace GaoApp.Web.HealthChecks;

public sealed class DatabaseHealthCheckOptions
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan MaximumTimeout = TimeSpan.FromSeconds(30);

    public TimeSpan Timeout { get; set; } = DefaultTimeout;
}
