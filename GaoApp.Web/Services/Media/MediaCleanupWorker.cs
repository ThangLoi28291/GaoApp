using System.Collections.Concurrent;
using GaoApp.Application.Common;
using GaoApp.Application.Common.Options;
using GaoApp.Application.DTOs.Media;
using GaoApp.Infrastructure.Data;
using GaoApp.Infrastructure.Services.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.Media;

public sealed record MediaCleanupRun(DateTime AtUtc, MediaCleanupResult Result);
public sealed class MediaCleanupStatus
{
    private readonly ConcurrentDictionary<int, MediaCleanupRun> runs = new();
    public MediaCleanupRun? Get(int storeId) => runs.GetValueOrDefault(storeId);
    public void Record(int storeId, MediaCleanupResult result) => runs[storeId] = new(DateTime.UtcNow, result);
}

public sealed class MediaCleanupWorker(IServiceScopeFactory scopes, IOptions<MediaCleanupOptions> options,
    MediaCleanupStatus status, ILogger<MediaCleanupWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        // Let startup validation finish before touching storage or the database.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Media cleanup sweep failed; will retry at the next interval."); }
            try { await Task.Delay(TimeSpan.FromMinutes(options.Value.IntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var discovery = scopes.CreateAsyncScope();
        var db = discovery.ServiceProvider.GetRequiredService<AppDbContext>();
        // Cross-tenant discovery reads store identifiers only; every mutation
        // runs with an explicit store and a fresh scope. Skip deleted stores.
        var stores = await db.Stores.IgnoreQueryFilters().AsNoTracking().Where(s => !s.IsDeleted)
            .Select(s => new { s.Id, s.SubDomain }).ToListAsync(ct);
        foreach (var store in stores)
        {
            var total = new MediaCleanupResult(0, 0, 0, 0, 0, 0);
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    await using var scope = scopes.CreateAsyncScope();
                    scope.ServiceProvider.GetRequiredService<ITenantContextWriter>().SetStore(store.Id, store.SubDomain);
                    var batch = await scope.ServiceProvider.GetRequiredService<MediaLibraryService>().SweepAsync(total.LastId, ct);
                    total = new(total.Scanned + batch.Scanned, total.Scheduled + batch.Scheduled, total.Deleted + batch.Deleted,
                        total.Failed + batch.Failed, total.Kept + batch.Kept, batch.LastId);
                    if (batch.Scanned < options.Value.BatchSize) break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                total = total with { Failed = total.Failed + 1 };
                logger.LogWarning(ex, "Media sweep failed for Store {StoreId}.", store.Id);
            }
            status.Record(store.Id, total);
            logger.LogInformation("Media sweep Store {StoreId}: scanned {Scanned}, scheduled {Scheduled}, deleted {Deleted}, failed {Failed}.",
                store.Id, total.Scanned, total.Scheduled, total.Deleted, total.Failed);
        }
    }
}
