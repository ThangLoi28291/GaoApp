using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using GaoApp.Application.Common.Exceptions;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;

namespace GaoApp.Infrastructure.Data;

// Per-context AND per-transaction freshness; a previous transaction never certifies the next one.
internal static class InventoryPostingLockState
{
    private sealed class State
    {
        public Guid TransactionId;
        public HashSet<object> Seen { get; } = new(ReferenceEqualityComparer.Instance);
        public Dictionary<(int StoreId, int WarehouseId, int VariantId), InventoryBalance> Balances { get; } = [];
    }
    private static readonly ConditionalWeakTable<AppDbContext, State> States = new();
    private static State? Current(AppDbContext db)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null) return null;
        var state = States.GetOrCreateValue(db);
        if (state.TransactionId != transaction.TransactionId)
        { state.TransactionId = transaction.TransactionId; state.Seen.Clear(); state.Balances.Clear(); }
        return state;
    }
    public static void MarkOwned(AppDbContext db, object entity) => Current(db)?.Seen.Add(entity);
    public static InventoryBalance? LockedBalance(AppDbContext db, int storeId, int warehouseId, int variantId)
    {
        var balance = Current(db)?.Balances.GetValueOrDefault((storeId, warehouseId, variantId));
        return balance is not null && db.Entry(balance).State != EntityState.Detached ? balance : null;
    }
    public static void MarkBalanceLocked(AppDbContext db, InventoryBalance balance)
    {
        var state = Current(db) ?? throw new InvalidOperationException("Inventory locking requires a transaction.");
        state.Balances[(balance.StoreId, balance.WarehouseId, balance.ProductVariantId)] = balance;
    }

    // A no-tracking query executed AFTER the balance lock already supplies fresh
    // database values. Merge its snapshots in one pass instead of reloading each
    // row. Preserve changes owned by this transaction and reject dirty state that
    // predates its first protected read, just as RefreshOnceAsync does.
    public static List<T> TrackFresh<T>(AppDbContext db, IReadOnlyList<T> snapshots) where T : BaseEntity
    {
        var state = Current(db) ?? throw new InvalidOperationException("Fresh posting reads require a transaction.");
        if (snapshots.Count == 0) return [];
        var ids = snapshots.Select(x => x.Id).ToHashSet();
        var tracked = db.ChangeTracker.Entries<T>().Where(x => ids.Contains(x.Entity.Id)).ToDictionary(x => x.Entity.Id);
        foreach (var snapshot in snapshots)
            if (tracked.TryGetValue(snapshot.Id, out var entry) && !state.Seen.Contains(entry.Entity) &&
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                throw new BusinessRuleException("TRACKED_STATE_DIRTY: inventory state changed before its first locked refresh.");
        var result = new List<T>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            if (tracked.TryGetValue(snapshot.Id, out var entry))
            {
                if (!state.Seen.Contains(entry.Entity))
                {
                    entry.CurrentValues.SetValues(snapshot);
                    entry.OriginalValues.SetValues(snapshot);
                    entry.State = EntityState.Unchanged;
                }
                state.Seen.Add(entry.Entity);
                result.Add(entry.Entity);
            }
            else
            {
                db.Entry(snapshot).State = EntityState.Unchanged;
                state.Seen.Add(snapshot);
                result.Add(snapshot);
            }
        }
        return result;
    }
    public static async Task RefreshOnceAsync(AppDbContext db, object entity, CancellationToken ct)
    {
        var state = Current(db);
        if (state is null || state.Seen.Contains(entity)) return;
        var entry = db.Entry(entity);
        if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            throw new BusinessRuleException("TRACKED_STATE_DIRTY: inventory state changed before its first locked refresh.");
        await entry.ReloadAsync(ct);
        if (entry.State == EntityState.Detached) throw new BusinessRuleException("INVENTORY_SOURCE_MISSING: locked evidence was removed.");
        state.Seen.Add(entity);
    }
}
