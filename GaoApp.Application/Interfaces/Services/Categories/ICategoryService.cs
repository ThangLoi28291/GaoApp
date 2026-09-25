using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Categories;

namespace GaoApp.Application.Interfaces.Services.Categories;

public interface ICategoryService
{
    Task<PagedResult<CategoryListItemDto>> GetPagedAsync(string? search, int page, int pageSize, CancellationToken ct = default);

    Task<PagedResult<CategoryListItemDto>> GetPagedAsync(
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        CancellationToken ct = default);

    Task<Result<CategoryEditDto>> GetForEditAsync(int id, CancellationToken ct = default);

    Task<Result<int>> CreateAsync(CategoryEditDto dto, CancellationToken ct = default);

    Task<Result> UpdateAsync(CategoryEditDto dto, CancellationToken ct = default);

    Task<Result> ToggleStatusAsync(int id, CancellationToken ct = default);

    Task<Result> DeleteAsync(int id, CancellationToken ct = default);

    Task<List<(int Id, string Name)>> GetParentOptionsAsync(int? excludeId, CancellationToken ct = default);
}
