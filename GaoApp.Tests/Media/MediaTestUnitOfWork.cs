using GaoApp.Application.Interfaces.Common;

namespace GaoApp.Tests.Media;

// Only for pre-existing fake-repository read/null-validation tests. Actual
// commit/rollback behavior is covered against SQL Server in MediaLibrarySqlServerTests.
internal sealed class MediaTestUnitOfWork : IAppUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    public Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default) => Task.FromResult<IAppTransaction>(new Transaction());
    private sealed class Transaction : IAppTransaction
    {
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
