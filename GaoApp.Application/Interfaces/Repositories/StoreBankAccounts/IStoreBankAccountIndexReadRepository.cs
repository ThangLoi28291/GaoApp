using GaoApp.Application.DTOs.StoreBankAccounts;

namespace GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;

public interface IStoreBankAccountIndexReadRepository
{
    Task<StoreBankAccountIndexPageDto> QueryAsync(
        int storeId,
        StoreBankAccountIndexQueryRequest request,
        CancellationToken ct = default);
}
