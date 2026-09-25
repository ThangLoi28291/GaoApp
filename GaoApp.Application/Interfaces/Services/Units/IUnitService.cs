using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Units;

namespace GaoApp.Application.Interfaces.Services.Units;

public interface IUnitService
{
    Task<PagedResult<UnitListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<UnitListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Result<UnitEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<Result<int>> CreateAsync(
        int storeId,
        CreateUnitRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        int storeId,
        UpdateUnitRequest dto,
        int? userId,
        CancellationToken ct = default);

    Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);
}