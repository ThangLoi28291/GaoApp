using GaoApp.Application.Common.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.HealthChecks;

/// <summary>
/// Health check kiểm tra storage local có dùng được không.
/// Áp dụng cho thư mục upload local.
/// </summary>
public class StorageHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;
    private readonly StorageOptions _storageOptions;
    private readonly ILogger<StorageHealthCheck> _logger;

    public StorageHealthCheck(
        IWebHostEnvironment environment,
        IOptions<StorageOptions> storageOptions,
        ILogger<StorageHealthCheck> logger)
    {
        _environment = environment;
        _storageOptions = storageOptions.Value;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_storageOptions.UploadRoot))
            {
                return Task.FromResult(
                    HealthCheckResult.Unhealthy("Storage:UploadRoot is missing."));
            }

            var absolutePath = Path.IsPathRooted(_storageOptions.UploadRoot)
                ? _storageOptions.UploadRoot
                : Path.Combine(_environment.ContentRootPath, _storageOptions.UploadRoot);

            if (!Directory.Exists(absolutePath))
            {
                if (_storageOptions.CreateIfMissing)
                {
                    Directory.CreateDirectory(absolutePath);
                }
                else
                {
                    return Task.FromResult(
                        HealthCheckResult.Unhealthy($"Upload directory does not exist: {absolutePath}"));
                }
            }

            var testFile = Path.Combine(absolutePath, $".healthcheck_{Guid.NewGuid():N}.tmp");

            File.WriteAllText(testFile, "health-check");
            File.Delete(testFile);

            return Task.FromResult(
                HealthCheckResult.Healthy($"Storage is writable: {absolutePath}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Storage health check failed.");
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Storage health check failed.", ex));
        }
    }
}