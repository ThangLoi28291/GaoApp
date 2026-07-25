using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.HealthChecks;

/// <summary>
/// Health check kiểm tra database có kết nối được hay không.
/// Dùng cho readiness check.
/// </summary>
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly IDatabaseConnectionProbe _connectionProbe;
    private readonly ILogger<DatabaseHealthCheck> _logger;
    private readonly TimeSpan _timeout;

    public DatabaseHealthCheck(
        IDatabaseConnectionProbe connectionProbe,
        ILogger<DatabaseHealthCheck> logger,
        IOptions<DatabaseHealthCheckOptions> options)
    {
        _connectionProbe = connectionProbe;
        _logger = logger;
        _timeout = options.Value.Timeout;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            var canConnect = await _connectionProbe.CanConnectAsync(
                timeoutSource.Token);

            cancellationToken.ThrowIfCancellationRequested();

            if (timeoutSource.IsCancellationRequested)
            {
                return CreateTimeoutResult();
            }

            if (canConnect)
            {
                return HealthCheckResult.Healthy("Database connection is healthy.");
            }

            return HealthCheckResult.Unhealthy("Database connection failed.");
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested &&
                  timeoutSource.IsCancellationRequested)
        {
            return CreateTimeoutResult();
        }
        catch (Exception)
            when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (Exception)
            when (timeoutSource.IsCancellationRequested)
        {
            return CreateTimeoutResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                "Database health check failed with {ExceptionType}.",
                ex.GetType().Name);

            return HealthCheckResult.Unhealthy("Database connection failed.");
        }
    }

    private HealthCheckResult CreateTimeoutResult()
    {
        _logger.LogWarning(
            "Database health check timed out after {TimeoutMilliseconds} ms.",
            _timeout.TotalMilliseconds);

        return HealthCheckResult.Unhealthy(
            "Database readiness check timed out.");
    }
}
