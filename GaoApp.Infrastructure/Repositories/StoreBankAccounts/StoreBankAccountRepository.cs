using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Domain.Entities;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.StoreBankAccounts;

public class StoreBankAccountRepository : IStoreBankAccountRepository
{
    private readonly AppDbContext _db;

    public StoreBankAccountRepository(AppDbContext db)
    {
        _db = db;
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
        var defaults = await _db.StoreBankAccounts
            .Where(x => x.IsDefault)
            .ToListAsync(ct);

        foreach (var item in defaults)
        {
            item.IsDefault = false;
        }
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