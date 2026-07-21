using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;

public interface IStoreBankAccountRepository
{
    Task<List<StoreBankAccount>> GetListAsync(CancellationToken ct = default);

    Task<StoreBankAccount?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<bool> ExistsAccountNumberAsync(
        string accountNumber,
        int? ignoreId = null,
        CancellationToken ct = default);

    Task AddAsync(StoreBankAccount entity, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);

    Task ClearDefaultAsync(CancellationToken ct = default);
    Task<List<StoreBankAccount>> GetActiveListAsync(CancellationToken ct = default);

    Task<StoreBankAccount?> GetDefaultActiveAsync(CancellationToken ct = default);
}