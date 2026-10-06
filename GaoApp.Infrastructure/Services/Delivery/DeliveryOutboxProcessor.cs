using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Services.Delivery;

// A foundation checkpoint consumer. Future monitor/integration consumers use their own receipt name.
// Its only effect is the durable receipt; no order, stock, payment or monitor mutation.
public sealed class DeliveryOutboxProcessor(AppDbContext db)
{
    public const string FoundationConsumer = "delivery-foundation-v1";
    public async Task<int> ProcessPendingAsync(string consumer = FoundationConsumer, int take = 50, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(consumer) || consumer.Length > 100) throw new ArgumentException("Invalid consumer.", nameof(consumer));
        var messages = await db.DeliveryOutboxMessages.AsNoTracking()
            .Where(x => !db.DeliveryOutboxReceipts.Any(r => r.StoreId == x.StoreId && r.EventId == x.EventId && r.Consumer == consumer))
            .OrderBy(x => x.Id).Take(Math.Clamp(take, 1, 100)).Select(x => new { x.StoreId, x.EventId }).ToListAsync(ct);
        var count = 0;
        foreach (var message in messages)
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                await DeliverySqlLock.AcquireAsync(db, "consumer:" + message.StoreId + ":" + message.EventId + ":" + consumer, ct);
                if (!await db.DeliveryOutboxReceipts.AnyAsync(x => x.StoreId == message.StoreId && x.EventId == message.EventId && x.Consumer == consumer, ct))
                {
                    db.DeliveryOutboxReceipts.Add(new() { StoreId = message.StoreId, EventId = message.EventId, Consumer = consumer });
                    await db.SaveChangesAsync(ct); count++;
                }
                await tx.CommitAsync(ct);
            }
            catch { await tx.RollbackAsync(CancellationToken.None); db.ChangeTracker.Clear(); throw; }
        }
        return count;
    }
}
