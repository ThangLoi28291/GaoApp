namespace GaoApp.Application.Interfaces.Common;

/// <summary>
/// Transaction abstraction cho tầng Application.
/// 
/// Mục đích:
/// - Application không phụ thuộc trực tiếp EF Core / AppDbContext
/// - chỉ cần biết mở transaction, commit, rollback
/// 
/// Lưu ý:
/// - Transaction này sẽ được tạo ở Infrastructure
/// - Service cấp cao như POSService sẽ dùng nó để bọc nhiều thao tác DB
/// </summary>
public interface IAppTransaction : IAsyncDisposable
{
    Task AfterCommitAsync(Func<CancellationToken, Task> action, CancellationToken ct = default) => action(ct);
    /// <summary>
    /// Commit transaction.
    /// Toàn bộ thay đổi trong transaction sẽ được xác nhận.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rollback transaction.
    /// Toàn bộ thay đổi trong transaction sẽ bị hoàn tác.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
