using GaoApp.Application.DTOs.StoreBankAccounts;


namespace GaoApp.Application.Interfaces.Services.StoreBankAccounts;

public interface IStoreBankAccountService
{
    Task<List<StoreBankAccountUpsertDto>> GetListAsync(CancellationToken ct = default);

    Task<StoreBankAccountUpsertDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<int> CreateAsync(
        StoreBankAccountUpsertDto dto,
        CancellationToken ct = default);

    Task UpdateAsync(
        StoreBankAccountUpsertDto dto,
        CancellationToken ct = default);

    Task ToggleStatusAsync(
        int id,
        CancellationToken ct = default);

    Task SetDefaultAsync(
        int id,
        CancellationToken ct = default);
}
