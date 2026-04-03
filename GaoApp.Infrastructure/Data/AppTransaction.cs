using GaoApp.Application.Interfaces.Common;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Implementation cụ thể của IAppTransaction bằng EF Core transaction.
/// 
/// Đây là lớp bridge:
/// - Application chỉ biết IAppTransaction
/// - Infrastructure dùng IDbContextTransaction thật sự của EF Core
/// </summary>
public sealed class AppTransaction : IAppTransaction
{
    private readonly IDbContextTransaction _transaction;

    public AppTransaction(IDbContextTransaction transaction)
    {
        _transaction = transaction;
    }

    /// <summary>
    /// Commit transaction EF Core.
    /// </summary>
    public async Task CommitAsync(CancellationToken ct = default)
    {
        await _transaction.CommitAsync(ct);
    }

    /// <summary>
    /// Rollback transaction EF Core.
    /// </summary>
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        await _transaction.RollbackAsync(ct);
    }

    /// <summary>
    /// Giải phóng transaction.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
    }
}