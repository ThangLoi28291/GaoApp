using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GaoApp.Web.HealthChecks;

/// <summary>
/// Extension đăng ký health checks cho web app.
/// </summary>
public static class HealthCheckRegistration
{
    public static IServiceCollection AddGaoAppHealthChecks(this IServiceCollection services)
    {
        services
            .AddOptions<DatabaseHealthCheckOptions>()
            .Validate(
                options =>
                    options.Timeout > TimeSpan.Zero &&
                    options.Timeout <= DatabaseHealthCheckOptions.MaximumTimeout,
                $"Database health check timeout phải lớn hơn 0 và không vượt quá " +
                $"{DatabaseHealthCheckOptions.MaximumTimeout.TotalSeconds} giây.")
            .ValidateOnStart();

        services.AddSingleton<
            IDatabaseConnectionFactory,
            SqlDatabaseConnectionFactory>();
        services.AddScoped<IDatabaseConnectionProbe, DatabaseConnectionProbe>();

        services.AddHealthChecks()
            // Liveness: app sống
            .AddCheck("self", () => HealthCheckResult.Healthy("Application is alive."), tags: new[] { "live" })

            // Readiness: DB
            .AddCheck<DatabaseHealthCheck>("database", tags: new[] { "ready" })

            // Readiness: storage
            .AddCheck<StorageHealthCheck>("storage", tags: new[] { "ready" });

        return services;
    }
}
