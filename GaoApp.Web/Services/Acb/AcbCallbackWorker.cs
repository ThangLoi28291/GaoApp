using GaoApp.Application.Common;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Web.Services.Acb;

public sealed class AcbCallbackWorker(IServiceScopeFactory scopes, AcbCallbackSignal signal,
    AcbCallbackDiagnostics diagnostics) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await signal.WaitAsync(stoppingToken);
                await using var discovery = scopes.CreateAsyncScope();
                var db = discovery.ServiceProvider.GetRequiredService<AppDbContext>();
                // Cross-store discovery reads identifiers only. Processing always uses a fresh tenant scope.
                var due = await db.Set<AcbCallbackReceipt>().AsNoTracking()
                    .Where(x => !x.IsDeleted && x.ProcessedAtUtc == null && x.NextAttemptAtUtc <= DateTime.UtcNow && x.Store != null && x.Store.IsActive)
                    .OrderBy(x => x.NextAttemptAtUtc).ThenBy(x => x.Id).Take(50)
                    .Select(x => new { x.Id, x.StoreId, x.Store!.SubDomainNormalized }).ToListAsync(stoppingToken);
                await Parallel.ForEachAsync(due, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = stoppingToken }, async (item, ct) =>
                {
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContextWriter>().SetStore(item.StoreId, item.SubDomainNormalized);
                    await scope.ServiceProvider.GetRequiredService<AcbCallbackInbox>().ProcessAsync(item.Id, ct);
                });
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                diagnostics.WorkerFailure(error);
                try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }
}
