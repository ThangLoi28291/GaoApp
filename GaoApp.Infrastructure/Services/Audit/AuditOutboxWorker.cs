using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Services.Audit;

public sealed class AuditOutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<AuditOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("AuditOutbox:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        var nextBacklogWarning = DateTime.MinValue;
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var discovery = scopes.CreateAsyncScope();
                    var db = discovery.ServiceProvider.GetRequiredService<AppDbContext>();
                    int[] stores;
                    var now = DateTime.UtcNow;
                    using (discovery.ServiceProvider.GetRequiredService<TenantContext>()
                        .BeginSystemAccess(SystemDataAccessPurpose.AuditOutboxDiscovery))
                    {
                        var pending = db.AuditOutbox.AsNoTracking().Where(x => x.ProcessedAtUtc == null);
                        stores = await pending.Where(x => x.NextAttemptAtUtc <= now).OrderBy(x => x.CreatedAtUtc)
                            .Select(x => x.StoreId).Take(100).Distinct().ToArrayAsync(stoppingToken);
                        if (now >= nextBacklogWarning)
                        {
                            var oldest = await pending.MinAsync(x => (DateTime?)x.CreatedAtUtc, stoppingToken);
                            var count = await pending.CountAsync(stoppingToken);
                            if (count >= 1000 || oldest < now.AddMinutes(-5))
                                logger.LogWarning("Audit outbox backlog. PendingCount={PendingCount}; OldestCreatedAtUtc={OldestCreatedAtUtc}", count, oldest);
                            nextBacklogWarning = now.AddMinutes(1);
                        }
                    }
                    foreach (var storeId in stores)
                    {
                        await using var scope = scopes.CreateAsyncScope();
                        scope.ServiceProvider.GetRequiredService<TenantContext>().SetStore(storeId, "audit-outbox");
                        await scope.ServiceProvider.GetRequiredService<AuditOutboxProcessor>().ProcessPendingAsync(ct: stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    logger.LogError("Audit outbox worker unavailable; persisted evidence will be retried. ExceptionType={ExceptionType}", ex.GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
    }
}
