using GaoApp.Application.DTOs.StoreBankAccounts;
using GaoApp.Application.Interfaces.Repositories.StoreBankAccounts;
using GaoApp.Application.Interfaces.Services.StoreBankAccounts;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Services.StoreBankAccounts;

public class StoreBankAccountService : IStoreBankAccountService
{
    private readonly IStoreBankAccountRepository _repository;

    public StoreBankAccountService(IStoreBankAccountRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<StoreBankAccountUpsertDto>> GetListAsync(CancellationToken ct = default)
    {
        var items = await _repository.GetListAsync(ct);

        return items.Select(MapToDto).ToList();
    }

    public async Task<StoreBankAccountUpsertDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);

        return entity == null ? null : MapToDto(entity);
    }

    public Task<int> CreateAsync(StoreBankAccountUpsertDto dto, CancellationToken ct = default)
        => _repository.ExecuteStoreWriteAsync(() => CreateCoreAsync(dto, ct), ct);

    public Task UpdateAsync(StoreBankAccountUpsertDto dto, CancellationToken ct = default)
        => _repository.ExecuteStoreWriteAsync(async () => { await UpdateCoreAsync(dto, ct); return true; }, ct);

    public Task ToggleStatusAsync(int id, CancellationToken ct = default)
        => _repository.ExecuteStoreWriteAsync(async () => { await ToggleStatusCoreAsync(id, ct); return true; }, ct);

    public Task SetDefaultAsync(int id, CancellationToken ct = default)
        => _repository.ExecuteStoreWriteAsync(async () => { await SetDefaultCoreAsync(id, ct); return true; }, ct);

    private async Task<int> CreateCoreAsync(
        StoreBankAccountUpsertDto dto,
        CancellationToken ct = default)
    {
        if (dto.IsDefault && !dto.IsActive)
            throw new InvalidOperationException("Không thể đặt mặc định tài khoản ngân hàng đang tắt.");

        var accountNumber = dto.AccountNumber.Trim();

        if (await _repository.ExistsAccountNumberAsync(accountNumber, null, ct))
        {
            throw new InvalidOperationException("Số tài khoản ngân hàng đã tồn tại.");
        }

        if (dto.IsDefault)
        {
            await _repository.ClearDefaultAsync(ct);
        }

        var entity = new StoreBankAccount
        {
            BankCode = dto.BankCode.Trim(),
            BankName = dto.BankName.Trim(),
            AccountNumber = accountNumber,
            AccountName = dto.AccountName.Trim(),
            IsDefault = dto.IsDefault,
            IsActive = dto.IsActive,
            VietQrBankBin = dto.VietQrBankBin?.Trim(),
            NoteTemplate = string.IsNullOrWhiteSpace(dto.NoteTemplate)
                ? "POS-{OrderId}-{QrRequestId}"
                : dto.NoteTemplate.Trim(),
            QrRenderMode = dto.QrRenderMode,
            ConfirmMode = dto.ConfirmMode,
            ProviderCode = string.IsNullOrWhiteSpace(dto.ProviderCode)
                ? "LOCAL"
                : dto.ProviderCode.Trim()
        };

        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);

        return entity.Id;
    }

    private async Task UpdateCoreAsync(
        StoreBankAccountUpsertDto dto,
        CancellationToken ct = default)
    {
        if (!dto.Id.HasValue)
        {
            throw new InvalidOperationException("Thiếu Id tài khoản ngân hàng.");
        }

        var entity = await _repository.GetByIdAsync(dto.Id.Value, ct);

        if (entity == null)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản ngân hàng.");
        }

        if (dto.IsDefault && !dto.IsActive)
            throw new InvalidOperationException("Không thể đặt mặc định tài khoản ngân hàng đang tắt.");

        var accountNumber = dto.AccountNumber.Trim();

        if (await _repository.ExistsAccountNumberAsync(accountNumber, dto.Id.Value, ct))
        {
            throw new InvalidOperationException("Số tài khoản ngân hàng đã tồn tại.");
        }

        if (dto.IsDefault)
        {
            await _repository.ClearDefaultAsync(ct);
        }

        entity.BankCode = dto.BankCode.Trim();
        entity.BankName = dto.BankName.Trim();
        entity.AccountNumber = accountNumber;
        entity.AccountName = dto.AccountName.Trim();
        entity.IsDefault = dto.IsDefault;
        entity.IsActive = dto.IsActive;
        entity.VietQrBankBin = dto.VietQrBankBin?.Trim();
        entity.NoteTemplate = string.IsNullOrWhiteSpace(dto.NoteTemplate)
            ? "POS-{OrderId}-{QrRequestId}"
            : dto.NoteTemplate.Trim();
        entity.QrRenderMode = dto.QrRenderMode;
        entity.ConfirmMode = dto.ConfirmMode;
        entity.ProviderCode = string.IsNullOrWhiteSpace(dto.ProviderCode)
            ? "LOCAL"
            : dto.ProviderCode.Trim();

        await _repository.SaveChangesAsync(ct);
    }

    private async Task ToggleStatusCoreAsync(
        int id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản ngân hàng.");
        }

        entity.IsActive = !entity.IsActive;
        if (!entity.IsActive) entity.IsDefault = false;

        await _repository.SaveChangesAsync(ct);
    }

    private async Task SetDefaultCoreAsync(
        int id,
        CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);

        if (entity == null)
        {
            throw new InvalidOperationException("Không tìm thấy tài khoản ngân hàng.");
        }

        if (!entity.IsActive)
        {
            throw new InvalidOperationException("Không thể đặt mặc định tài khoản ngân hàng đang tắt.");
        }

        await _repository.ClearDefaultAsync(ct);

        entity.IsDefault = true;

        await _repository.SaveChangesAsync(ct);
    }

    private static StoreBankAccountUpsertDto MapToDto(StoreBankAccount entity)
    {
        return new StoreBankAccountUpsertDto
        {
            Id = entity.Id,
            BankCode = entity.BankCode,
            BankName = entity.BankName,
            AccountNumber = entity.AccountNumber,
            AccountName = entity.AccountName,
            IsDefault = entity.IsDefault,
            IsActive = entity.IsActive,
            VietQrBankBin = entity.VietQrBankBin,
            NoteTemplate = entity.NoteTemplate,
            QrRenderMode = entity.QrRenderMode,
            ConfirmMode = entity.ConfirmMode,
            ProviderCode = entity.ProviderCode
        };
    }
}