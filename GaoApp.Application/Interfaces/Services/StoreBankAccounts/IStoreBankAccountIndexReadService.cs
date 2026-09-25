using GaoApp.Application.DTOs.StoreBankAccounts;

namespace GaoApp.Application.Interfaces.Services.StoreBankAccounts;

public interface IStoreBankAccountIndexReadService
{
    Task<StoreBankAccountIndexPageDto> GetPageAsync(
        int storeId,
        StoreBankAccountIndexQueryRequest request,
        CancellationToken ct = default);
}
