using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Data;

// Per-context AND per-transaction freshness; a previous transaction never certifies the next one.
internal static class InventoryPostingLockState
{
    private sealed class State
    {
        public Guid TransactionId;
        public HashSet<object> Seen { get; } = new(ReferenceEqualityComparer.Instance);
    }
    private static readonly ConditionalWeakTable<AppDbContext, State> States = new();
    private static State? Current(AppDbContext db)
    {
        var transaction = db.Database.CurrentTransaction;
        if (transaction is null) return null;
        var state = States.GetOrCreateValue(db);
        if (state.TransactionId != transaction.TransactionId)
        { state.TransactionId = transaction.TransactionId; state.Seen.Clear(); }
        return state;
    }
    public static void MarkOwned(AppDbContext db, object entity) => Current(db)?.Seen.Add(entity);
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
