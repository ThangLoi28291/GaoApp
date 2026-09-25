using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

/// <summary>
/// Repository xử lý Product.
///
/// Ghi chú kiến trúc barcode mới:
/// - ProductVariant không còn Barcode
/// - Khi tạo product mới có thể cần tạo kèm:
///   + ProductVariant mặc định
///   + ProductUnitConversion base unit
///   + ProductVariantUnitBarcode cho base unit
/// </summary>
public interface IProductRepository
{
    Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<PagedResult<ProductListItemDto>> GetPagedAsync(
        int storeId,
        string? search,
        int? categoryId,
        bool? isActive,
        bool? isSellable,
        int page,
        int pageSize,
        CancellationToken ct = default);

    Task<(int TotalItems, int PosAllowedItems, int NotForPosItems, int InactiveItems)> GetSummaryAsync(
        int storeId,
        CancellationToken ct = default);

    Task<Product?> GetDetailAsync(
        int storeId,
        int id,
        CancellationToken ct = default);

    Task<bool> ExistsAliasAsync(
        int storeId,
        string alias,
        int? excludeId,
        CancellationToken ct = default);

    Task AddAsync(
        Product entity,
        CancellationToken ct = default);

    /// <summary>
    /// Thêm ProductUnitConversion.
    /// Dùng trong flow tạo product mặc định để sinh base unit conversion cho variant mặc định.
    /// </summary>
    Task AddProductUnitConversionAsync(
        ProductUnitConversion entity,
        CancellationToken ct = default);

    /// <summary>
    /// Thêm ProductVariantUnitBarcode.
    /// Dùng trong flow tạo product mặc định để sinh barcode nội bộ cho base unit.
    /// </summary>
    Task AddProductVariantUnitBarcodeAsync(
        ProductVariantUnitBarcode entity,
        CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<bool> ToggleStatusAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);

    Task<bool> SoftDeleteAsync(
        int storeId,
        int id,
        int? userId,
        CancellationToken ct = default);
    Task<bool> ExistsVariantUnitBarcodeAsync(
    int storeId,
    string barcode,
    CancellationToken ct = default);
}
