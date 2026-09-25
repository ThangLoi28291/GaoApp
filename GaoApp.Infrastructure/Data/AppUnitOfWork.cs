using GaoApp.Application.Interfaces.Common;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Implementation cụ thể của IAppUnitOfWork.
/// 
/// Nhiệm vụ:
/// - dùng AppDbContext để mở transaction DB
/// - bọc transaction EF Core thành IAppTransaction
/// 
/// Lưu ý:
/// - không chứa business logic
/// - chỉ là lớp hạ tầng để hỗ trợ transaction cho Application
/// </summary>
public sealed class AppUnitOfWork : IAppUnitOfWork
{
    private readonly AppDbContext _db;
    private readonly Queue<Func<CancellationToken, Task>> afterCommit = new();
    private bool rollbackRequested;

    public AppUnitOfWork(AppDbContext db)
    {
        _db = db;
    }
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    /// <summary>
    /// Mở transaction mới từ AppDbContext.
    /// </summary>
    public async Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        // A POS operation may wrap several service calls in one durable retry transaction.
        // The outer owner alone commits/disposes; exceptions propagate to its rollback.
        if (_db.Database.CurrentTransaction != null)
            return new ParticipatingTransaction(afterCommit, () => rollbackRequested = true);
        var tx = await _db.Database.BeginTransactionAsync(ct);
        return new AppTransaction(tx);
    }

    public async Task RunDeferredActionsAsync(CancellationToken ct = default)
    {
        while (afterCommit.TryDequeue(out var action)) await action(ct);
    }

    public void EnsureCanCommit()
    {
        if (rollbackRequested) throw new InvalidOperationException("A participating service requested rollback; the outer POS operation cannot commit.");
    }

    private sealed class ParticipatingTransaction(Queue<Func<CancellationToken, Task>> afterCommit, Action requestRollback) : IAppTransaction
    {
        public Task AfterCommitAsync(Func<CancellationToken, Task> action, CancellationToken ct = default)
        { afterCommit.Enqueue(action); return Task.CompletedTask; }
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) { requestRollback(); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
