using GaoApp.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Storage;

namespace GaoApp.Infrastructure.Data;

/// <summary>
/// Implementation UnitOfWork dùng AppDbContext bên dưới.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;
    private IDbContextTransaction? _currentTransaction;
    private bool _ownsCurrentTransaction;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction != null)
            return;

        // Cho phép service con tham gia transaction do service cha đã mở
        // trên cùng AppDbContext, tránh lỗi nested transaction và commit sớm.
        if (_db.Database.CurrentTransaction != null)
        {
            _currentTransaction = _db.Database.CurrentTransaction;
            _ownsCurrentTransaction = false;
            return;
        }

        _currentTransaction = await _db.Database.BeginTransactionAsync(ct);
        _ownsCurrentTransaction = true;
    }

    public async Task CommitTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        if (!_ownsCurrentTransaction)
        {
            _currentTransaction = null;
            return;
        }

        await _currentTransaction.CommitAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
        _ownsCurrentTransaction = false;
    }

    public async Task RollbackTransactionAsync(CancellationToken ct = default)
    {
        if (_currentTransaction == null)
            return;

        if (!_ownsCurrentTransaction)
        {
            _currentTransaction = null;
            return;
        }

        await _currentTransaction.RollbackAsync(ct);
        await _currentTransaction.DisposeAsync();
        _currentTransaction = null;
        _ownsCurrentTransaction = false;
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
        => _db.SaveChangesAsync(ct);
}
