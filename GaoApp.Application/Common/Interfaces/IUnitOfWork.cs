namespace GaoApp.Application.Common.Interfaces;

/// <summary>
/// Abstraction cho transaction + save changes.
/// Service layer dùng interface này thay vì chạm trực tiếp AppDbContext.
/// </summary>
public interface IUnitOfWork
{
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitTransactionAsync(CancellationToken ct = default);
    Task RollbackTransactionAsync(CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}