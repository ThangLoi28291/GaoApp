using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

/// <summary>
/// Repository quản lý quy đổi đơn vị bán của ProductVariant.
/// </summary>
public interface IProductUnitConversionRepository
{
    /// <summary>
    /// Lấy conversion theo Id, kèm Unit + Barcodes + Variant + Product.
    /// </summary>
    Task<ProductUnitConversion?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ conversion của 1 variant, kèm Unit + Barcodes.
    /// </summary>
    Task<List<ProductUnitConversion>> GetByVariantIdAsync(int productVariantId, CancellationToken ct = default);

    /// <summary>
    /// Lấy conversion theo variant + unit.
    /// Dùng nhiều cho POS / StockDocument / InventoryAdjustment.
    /// </summary>
    Task<ProductUnitConversion?> GetByVariantAndUnitAsync(int productVariantId, int unitId, CancellationToken ct = default);

    /// <summary>
    /// Lấy conversion mặc định để bán.
    /// </summary>
    Task<ProductUnitConversion?> GetDefaultForSaleAsync(int productVariantId, CancellationToken ct = default);

    /// <summary>
    /// Lấy conversion đơn vị gốc của variant.
    /// </summary>
    Task<ProductUnitConversion?> GetBaseUnitAsync(int productVariantId, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra variant đã có conversion cho unit này chưa.
    /// </summary>
    Task<bool> ExistsByVariantAndUnitAsync(int productVariantId, int unitId, int? excludeId = null, CancellationToken ct = default);

    /// <summary>
    /// Thêm conversion mới.
    /// </summary>
    Task AddAsync(ProductUnitConversion entity, CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}