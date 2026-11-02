using GaoApp.Web.Services.Assistant;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GaoApp.Web.HealthChecks;

public sealed class AssistantStorageHealthCheck(IWebHostEnvironment environment, IConfiguration configuration) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { cancellationToken.ThrowIfCancellationRequested(); AssistantStorageSafety.Validate(environment, configuration); return Task.FromResult(HealthCheckResult.Healthy()); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return Task.FromResult(HealthCheckResult.Unhealthy("Persistent assistant storage unavailable.")); }
    }
}
