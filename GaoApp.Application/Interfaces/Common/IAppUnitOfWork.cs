namespace GaoApp.Application.Interfaces.Common;

/// <summary>
/// UnitOfWork abstraction cho tầng Application.
/// </summary>
public interface IAppUnitOfWork
{
    void EnsureCanCommit() { }
    Task RunDeferredActionsAsync(CancellationToken ct = default) => Task.CompletedTask;
    /// <summary>
    /// Lưu thay đổi hiện tại.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// Mở transaction mới.
    /// Service gọi hàm này sẽ chịu trách nhiệm commit / rollback.
    /// </summary>
    Task<IAppTransaction> BeginTransactionAsync(CancellationToken ct = default);
}
