namespace GaoApp.Infrastructure.Services.Invoices;

public sealed class ExternalHttpResilienceOptions
{
    public const string SectionName = "ExternalHttpResilience";
    public const int MaximumTimeoutSeconds = 60;
    public const int AutomaticRetryCount = 0;
    public static readonly TimeSpan AutomaticRetryDelay = TimeSpan.Zero;
    public const int SafeGetTransientRetryCount = 1;
    public static readonly TimeSpan SafeGetTransientRetryDelay =
        TimeSpan.FromMilliseconds(200);

    public int AuthenticationTimeoutSeconds { get; set; } = 20;
    public int SafeReadTimeoutSeconds { get; set; } = 20;
    public int NonIdempotentWriteTimeoutSeconds { get; set; } = 30;
    public int FileDownloadTimeoutSeconds { get; set; } = 45;

    public TimeSpan AuthenticationTimeout =>
        ToBoundedTimeout(AuthenticationTimeoutSeconds);

    public TimeSpan SafeReadTimeout =>
        ToBoundedTimeout(SafeReadTimeoutSeconds);

    public TimeSpan NonIdempotentWriteTimeout =>
        ToBoundedTimeout(NonIdempotentWriteTimeoutSeconds);

    public TimeSpan FileDownloadTimeout =>
        ToBoundedTimeout(FileDownloadTimeoutSeconds);

    public bool HasValidTimeouts() =>
        IsValid(AuthenticationTimeoutSeconds) &&
        IsValid(SafeReadTimeoutSeconds) &&
        IsValid(NonIdempotentWriteTimeoutSeconds) &&
        IsValid(FileDownloadTimeoutSeconds);

    private static bool IsValid(int seconds) =>
        seconds is > 0 and <= MaximumTimeoutSeconds;

    private static TimeSpan ToBoundedTimeout(int seconds)
    {
        if (!IsValid(seconds))
        {
            throw new InvalidOperationException(
                $"External HTTP timeout must be between 1 and {MaximumTimeoutSeconds} seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
