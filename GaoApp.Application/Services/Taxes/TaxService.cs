using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Taxes;
using GaoApp.Application.Interfaces.Repositories.Taxes;
using GaoApp.Application.Interfaces.Services.Taxes;
using GaoApp.Domain.Entities;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.Services.Taxes;

public sealed class TaxService : ITaxService
{
    private readonly ITaxRepository _repo;

    public TaxService(ITaxRepository repo)
    {
        _repo = repo;
    }

    public Task<PagedResult<TaxListItemDto>> GetPagedAsync(
        int storeId, string? search, int page, int pageSize, CancellationToken ct = default)
        => GetPagedAsync(storeId, search, status: null, page, pageSize, ct);

    public async Task<PagedResult<TaxListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(
            storeId,
            search,
            status,
            page,
            pageSize,
            ct);

        return new PagedResult<TaxListItemDto>
        {
            Page = page,
            PageSize = pageSize,
            TotalItems = total,
            Items = items.Select(x => new TaxListItemDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Rate = x.Rate,
                Status = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc
            }).ToList()
        };
    }

    public Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default)
        => _repo.GetSummaryAsync(storeId, ct);

    public async Task<TaxEditDto?> GetForEditAsync(int storeId, int id, CancellationToken ct = default)
    {
        var e = await _repo.GetByIdAsync(storeId, id, ct);
        if (e == null) return null;

        return new TaxEditDto
        {
            Id = e.Id,
            Code = e.Code,
            Name = e.Name,
            Rate = e.Rate,
            Status = e.IsActive,
            RowVersion = e.RowVersion
        };
    }

    public async Task<int> CreateAsync(int storeId, CreateTaxRequest dto, int? userId, CancellationToken ct = default)
    {
        var name = (dto.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Vui lòng nhập tên thuế");

        if (dto.Rate < 0 || dto.Rate > 100)
            throw new BusinessRuleException("Thuế suất phải nằm trong khoảng 0–100%.");

        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
            code = await GenerateTaxCodeAsync(storeId, ct);

        if (await _repo.ExistsCodeAsync(storeId, code, null, ct))
            throw new BusinessRuleException("Mã thuế đã tồn tại.");

        if (await _repo.ExistsNameAsync(storeId, name, null, ct))
            throw new BusinessRuleException("Tên thuế đã tồn tại.");

        var e = new Tax
        {
            StoreId = storeId,
            Code = code,
            Name = name,
            Rate = dto.Rate,
            IsActive = dto.Status,
            SortOrder = 0
        };

        await _repo.AddAsync(e, ct);
        await _repo.SaveChangesAsync(ct);
        return e.Id;
    }

    public async Task<bool> UpdateAsync(int storeId, UpdateTaxRequest dto, int? userId, CancellationToken ct = default)
    {
        var e = await _repo.GetByIdAsync(storeId, dto.Id, ct);
        if (e == null) return false;

        var code = (dto.Code ?? string.Empty).Trim().ToUpperInvariant();
        var name = (dto.Name ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new BusinessRuleException("Vui lòng nhập mã thuế");

        if (string.IsNullOrWhiteSpace(name))
            throw new BusinessRuleException("Vui lòng nhập tên thuế");

        if (dto.Rate < 0 || dto.Rate > 100)
            throw new BusinessRuleException("Thuế suất phải nằm trong khoảng 0–100%.");

        if (await _repo.ExistsCodeAsync(storeId, code, dto.Id, ct))
            throw new BusinessRuleException("Mã thuế đã tồn tại.");

        if (await _repo.ExistsNameAsync(storeId, name, dto.Id, ct))
            throw new BusinessRuleException("Tên thuế đã tồn tại.");

        if (dto.RowVersion is { Length: > 0 })
            e.RowVersion = dto.RowVersion;

        e.Code = code;
        e.Name = name;
        e.Rate = dto.Rate;
        e.IsActive = dto.Status;

        try
        {
            await _repo.SaveChangesAsync(ct);
            return true;
        }
        catch (ConcurrencyException)
        {
            throw new ConcurrencyException(
                "Dữ liệu đã được người khác thay đổi. Vui lòng tải lại và thử lại.");
        }
    }

    public async Task<bool> ToggleStatusAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var e = await _repo.GetByIdAsync(storeId, id, ct);
        if (e == null) return false;

        e.IsActive = !e.IsActive;
        await _repo.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(int storeId, int id, int? userId, CancellationToken ct = default)
    {
        var e = await _repo.GetByIdAsync(storeId, id, ct);
        if (e == null) return false;

        _repo.Remove(e);
        await _repo.SaveChangesAsync(ct);
        return true;
    }

    private async Task<string> GenerateTaxCodeAsync(int storeId, CancellationToken ct)
    {
        for (var i = 0; i < 50; i++)
        {
            var code = "TAX" + Random.Shared.Next(100000, 999999);
            if (!await _repo.ExistsCodeAsync(storeId, code, null, ct))
                return code;
        }

        return "TAX" + DateTime.UtcNow.ToString("yyMMddHHmmss");
    }
}
