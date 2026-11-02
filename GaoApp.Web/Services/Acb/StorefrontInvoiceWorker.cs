using GaoApp.Application.Common;
using GaoApp.Application.Storefront;
using GaoApp.Domain.Storefront;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Tenant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

// Recovers committed source documents after a process stops between sale commit and draft creation.
public sealed class StorefrontInvoiceWorker(IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<StorefrontInvoiceWorker> log, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!configuration.GetValue("StorefrontFulfillment:InvoiceWorkerEnabled", true)) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var discovery = scopes.CreateAsyncScope(); var db = discovery.ServiceProvider.GetRequiredService<AppDbContext>();
                using var grant = discovery.ServiceProvider.GetRequiredService<TenantContext>().BeginSystemAccess(SystemDataAccessPurpose.DeliveryOutboxDiscovery);
                var now = clock.GetUtcNow().UtcDateTime;
                var due = await db.Set<StorefrontInvoiceWork>().AsNoTracking().Where(x => x.CompletedAtUtc == null && x.NextAttemptAtUtc <= now && x.Store != null && x.Store.IsActive)
                    .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).Take(50).Select(x => new { x.StoreId, x.StorefrontOrderId, x.Store!.SubDomainNormalized }).ToListAsync(stoppingToken);
                foreach (var row in due)
                {
                    await using var scope = scopes.CreateAsyncScope(); scope.ServiceProvider.GetRequiredService<ITenantContextWriter>().SetStore(row.StoreId, row.SubDomainNormalized);
                    await scope.ServiceProvider.GetRequiredService<IStorefrontInvoiceProcessor>().ProcessAsync(row.StorefrontOrderId, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { log.LogWarning("Storefront invoice source work remains pending and will be retried; no document content is logged."); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
