using GaoApp.Infrastructure.Services.Delivery;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Delivery;

public sealed class DeliveryOutboxWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<DeliveryOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("DeliveryOutbox:Enabled", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    int[] storeIds;
                    using (scope.ServiceProvider.GetRequiredService<TenantContext>()
                        .BeginSystemAccess(SystemDataAccessPurpose.DeliveryOutboxDiscovery))
                    {
                        storeIds = await db.DeliveryOutboxMessages.AsNoTracking()
                            .Where(message => !db.DeliveryOutboxReceipts.Any(receipt => receipt.StoreId == message.StoreId &&
                                receipt.EventId == message.EventId && receipt.Consumer == DeliveryOutboxProcessor.FoundationConsumer))
                            .OrderBy(message => message.Id).Select(message => message.StoreId).Take(50).Distinct().ToArrayAsync(stoppingToken);
                    }
                    var remaining = 50;
                    foreach (var storeId in storeIds)
                    {
                        await using var storeScope = scopes.CreateAsyncScope();
                        var tenant = storeScope.ServiceProvider.GetRequiredService<TenantContext>();
                        tenant.SetStore(storeId, "delivery-outbox");
                        remaining -= await storeScope.ServiceProvider.GetRequiredService<DeliveryOutboxProcessor>()
                            .ProcessPendingAsync(take: remaining, ct: stoppingToken);
                        if (remaining <= 0) break;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Delivery outbox checkpoint failed; unacknowledged events remain pending."); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
    }
}
