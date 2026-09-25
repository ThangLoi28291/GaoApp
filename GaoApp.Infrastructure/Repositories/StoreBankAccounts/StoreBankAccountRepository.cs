using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Repositories.StoreBankAccounts;

public class StoreBankAccountRepository : IStoreBankAccountRepository
{
    private readonly AppDbContext _db;

    public StoreBankAccountRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<T> ExecuteStoreWriteAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        var storeId = _db.CurrentStoreId ?? throw new InvalidOperationException("A store is required for bank account writes.");
        if (storeId <= 0) throw new InvalidOperationException("Invalid store.");

        if (_db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Bank account writes must own their transaction.");
        await using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var resource = $"gaoapp:bank-account:{storeId}";
            await _db.Database.ExecuteSqlInterpolatedAsync($"""
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive',
                    @LockOwner='Transaction', @LockTimeout=5000;
                IF @result < 0 THROW 51000, 'Bank account store lock unavailable.', 1;
                """, ct);
            var result = await operation();
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (SqlException ex) when (ex.Number == 51000)
        { throw new ConflictAppException("Tài khoản ngân hàng đang được cập nhật. Vui lòng thử lại."); }
        catch (DbUpdateConcurrencyException)
        { throw new ConflictAppException("Tài khoản ngân hàng đã thay đổi. Vui lòng tải lại và thử lại."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { throw new ConflictAppException("Số tài khoản hoặc ngân hàng mặc định đã tồn tại. Vui lòng tải lại."); }
    }

    public async Task<List<StoreBankAccount>> GetListAsync(CancellationToken ct = default)
    {
        return await _db.StoreBankAccounts
            .AsNoTracking()
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.BankName)
            .ToListAsync(ct);
    }

    public async Task<StoreBankAccount?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _db.StoreBankAccounts
            .FirstOrDefaultAsync(x => x.Id == id, ct);
    }

    public async Task<bool> ExistsAccountNumberAsync(
      string accountNumber,
      int? ignoreId = null,
      CancellationToken ct = default)
    {
        var normalized = (accountNumber ?? string.Empty).Trim();

        return await _db.StoreBankAccounts
            .AnyAsync(x =>
                x.AccountNumber == normalized &&
                (!ignoreId.HasValue || x.Id != ignoreId.Value),
                ct);
    }

    public async Task AddAsync(StoreBankAccount entity, CancellationToken ct = default)
    {
        await _db.StoreBankAccounts.AddAsync(entity, ct);
    }

    public async Task ClearDefaultAsync(CancellationToken ct = default)
    {
        if (_db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Clearing bank defaults requires a store write transaction.");
        var defaults = await _db.StoreBankAccounts
            .Where(x => x.IsDefault)
            .ToListAsync(ct);

        foreach (var item in defaults)
        {
            item.IsDefault = false;
        }
        // Flush the old default before setting the new one to satisfy the unique index.
        // The enclosing store transaction rolls both saves back together on failure.
        if (defaults.Count > 0) await _db.SaveChangesAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<List<StoreBankAccount>> GetActiveListAsync(CancellationToken ct = default)
    {
        return await _db.StoreBankAccounts
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.BankName)
            .ToListAsync(ct);
    }

    public async Task<StoreBankAccount?> GetDefaultActiveAsync(CancellationToken ct = default)
    {
        return await _db.StoreBankAccounts
            .AsNoTracking()
            .Where(x => x.IsActive && x.IsDefault)
            .FirstOrDefaultAsync(ct);
    }
}
