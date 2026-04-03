using GaoApp.Application.Common;
using GaoApp.Application.DTOs.Products;
using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

public interface IProductVariantBarcodeHistoryRepository
{
    Task AddAsync(ProductVariantBarcodeHistory entity, CancellationToken ct = default);

    Task<PagedResult<BarcodeHistoryRowDto>> GetPagedAsync(
        int storeId,
        BarcodeHistoryQueryRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Lấy các bản ghi lịch sử gần nhất của 1 ProductUnitConversion.
    /// Dùng cho modal lịch sử barcode theo từng conversion.
    /// </summary>
    Task<List<ProductVariantBarcodeHistory>> GetRecentByConversionIdAsync(
        int storeId,
        int productUnitConversionId,
        int take = 20,
        CancellationToken ct = default);

    Task<ProductVariantBarcodeHistory?> FindByHistoricalBarcodeAsync(
        int storeId,
        string barcode,
        CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}