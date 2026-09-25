using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Brands;

namespace GaoApp.Application.Interfaces.Services.Brands;

/// <summary>
/// Service Brand chuẩn hóa theo Result Pattern.
/// Giữ request/response DTO cũ để không phá các caller hiện hữu như ProductController.
/// </summary>
public interface IBrandService
{
    Task<PagedResult<BrandListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<BrandListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        bool? status,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int ActiveItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy dữ liệu để edit.
    /// </summary>
    Task<Result<BrandEditDto>> GetForEditAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    /// <summary>
    /// Tạo mới brand, trả về Id brand vừa tạo.
    /// </summary>
    Task<Result<int>> CreateAsync(
        int storeId,
        CreateBrandRequest dto,
        int? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Cập nhật brand.
    /// </summary>
    Task<Result> UpdateAsync(
        int storeId,
        UpdateBrandRequest dto,
        int? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Toggle trạng thái hoạt động.
    /// Trả về trạng thái mới sau khi toggle.
    /// </summary>
    Task<Result<bool>> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    /// <summary>
    /// Soft delete.
    /// </summary>
    Task<Result> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);
}
