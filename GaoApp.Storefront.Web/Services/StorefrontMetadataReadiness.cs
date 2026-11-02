using System.Diagnostics;
using System.Text.Json;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Storefront.Web.Services;

public static class StorefrontMetadataReadiness
{
    // Metadata readiness for the real configured serving mode. Closed/unconfigured
    // hosts keep their existing behavior and do not require a database context.
    public static void Initialize(IServiceProvider services, IConfiguration configuration, IHostEnvironment environment, ILogger logger)
    {
        var policy = StorefrontHostPolicy.From(configuration, environment);
        if (policy.Mode == StorefrontRuntimeMode.Closed || !policy.Configured ||
            string.IsNullOrWhiteSpace(configuration.GetConnectionString("DefaultConnection"))) return;

        var started = Stopwatch.GetTimestamp();
        try
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // This is the same naturally unbound guest scope as the first host-gate
            // query, before that query binds its actual store. Never grant a tenant
            // or system identity to construct metadata.
            if (db.CurrentStoreId is not null || db.CanReadAllStores || db.CanWriteStoreData ||
                db.CurrentUserId is not null || db.IsCurrentUserAuthenticated)
                throw new InvalidOperationException("Startup metadata requires an unbound guest scope.");

            // No connection, query, tracking, SaveChanges, schema action or route
            // visit. EF retains its ordinary provider/model cache and dynamic
            // context-property query filters; no catalogue data is cached here.
            _ = db.Model.GetRelationalModel();
        }
        catch
        {
            logger.LogError("Storefront startup metadata initialization failed.");
            // Do not expose configuration/provider exception values. Throw before
            // Run/listening so health cannot announce this configured host ready.
            throw new InvalidOperationException("Storefront startup metadata initialization failed.");
        }

        logger.LogInformation("Storefront startup phase durations in milliseconds: {PhaseMilliseconds}",
            JsonSerializer.Serialize(new { startup_model = Math.Round(Stopwatch.GetElapsedTime(started).TotalMilliseconds, 3) }));
    }
}
