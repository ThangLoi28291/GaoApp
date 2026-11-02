using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GaoApp.Infrastructure.Services.Audit;

public sealed class AuditOutboxProcessor(AppDbContext db, IDbContextFactory<AuditLogDbContext> factory,
    ILogger<AuditOutboxProcessor> logger)
{
    public async Task<int> ProcessPendingAsync(int take = 50, CancellationToken ct = default)
    {
        var storeId = db.CurrentStoreId;
        if (storeId is not > 0) throw new InvalidOperationException("Audit delivery requires a concrete store scope.");
        var now = DateTime.UtcNow;
        var events = await db.AuditOutbox.AsNoTracking()
            .Where(x => x.StoreId == storeId && x.ProcessedAtUtc == null && x.NextAttemptAtUtc <= now)
            .OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.EventId).Take(Math.Clamp(take, 1, 100))
            .Select(x => x.EventId).ToArrayAsync(ct);
        var count = 0;
        foreach (var eventId in events)
        {
            try
            {
                await using var audit = await factory.CreateDbContextAsync(ct);
                await using var tx = await audit.Database.BeginTransactionAsync(ct);
                var resource = "gaoapp:audit:" + eventId.ToString("N");
                // Cross-process serialization. A failed/terminated worker releases this transaction-owned lock.
                await audit.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=5000; IF @r < 0 THROW 51011, 'Audit consumer lock unavailable.', 1;", ct);
                var message = await audit.AuditOutbox.SingleOrDefaultAsync(x => x.StoreId == storeId &&
                    x.EventId == eventId && x.ProcessedAtUtc == null && x.NextAttemptAtUtc <= now, ct);
                if (message is null) continue;
                if (!await audit.AuditLogs.AnyAsync(x => x.StoreId == storeId && x.EventId == eventId, ct))
                    audit.AuditLogs.Add(AuditOutboxEvidence.Read(message));
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.LastErrorType = null;
                await audit.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                count++;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogWarning("Audit delivery failed; evidence remains pending. StoreId={StoreId}; EventId={EventId}; ExceptionType={ExceptionType}",
                    storeId, eventId, ex.GetType().Name);
                try
                {
                    await using var retry = await factory.CreateDbContextAsync(ct);
                    var attempt = await retry.AuditOutbox.Where(x => x.StoreId == storeId && x.EventId == eventId && x.ProcessedAtUtc == null)
                        .Select(x => (int?)x.AttemptCount).SingleOrDefaultAsync(ct);
                    if (attempt is null) continue;
                    var next = DateTime.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(attempt.Value + 1, 9))));
                    await retry.AuditOutbox.Where(x => x.StoreId == storeId && x.EventId == eventId && x.ProcessedAtUtc == null)
                        .ExecuteUpdateAsync(set => set.SetProperty(x => x.AttemptCount, x => x.AttemptCount + 1)
                            .SetProperty(x => x.NextAttemptAtUtc, next).SetProperty(x => x.LastErrorType, ex.GetType().Name), ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception retryError)
                {
                    logger.LogError("Audit retry metadata unavailable; evidence remains pending. StoreId={StoreId}; EventId={EventId}; ExceptionType={ExceptionType}",
                        storeId, eventId, retryError.GetType().Name);
                }
            }
        }
        return count;
    }
}
