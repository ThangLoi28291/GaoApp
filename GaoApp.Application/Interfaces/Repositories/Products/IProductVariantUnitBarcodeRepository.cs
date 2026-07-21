using GaoApp.Domain.Entities;

namespace GaoApp.Application.Interfaces.Repositories.Products;

/// <summary>
/// Repository quản lý barcode theo đơn vị bán.
///
/// CHỐT KIẾN TRÚC:
/// - Đây là nguồn barcode duy nhất của hệ thống
/// - Mọi lookup barcode chính thức phải đi qua repository này
/// </summary>
public interface IProductVariantUnitBarcodeRepository
{
    /// <summary>
    /// Lấy barcode theo Id, kèm navigation đầy đủ để dùng cho edit / nghiệp vụ.
    /// </summary>
    Task<ProductVariantUnitBarcode?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Lấy toàn bộ barcode của 1 ProductUnitConversion.
    /// </summary>
    Task<List<ProductVariantUnitBarcode>> GetByConversionIdAsync(int productUnitConversionId, CancellationToken ct = default);

    /// <summary>
    /// Lấy barcode primary của 1 ProductUnitConversion.
    /// </summary>
    Task<ProductVariantUnitBarcode?> GetPrimaryByConversionIdAsync(int productUnitConversionId, CancellationToken ct = default);

    /// <summary>
    /// Tìm barcode chính xác theo store hiện tại.
    /// Trả về đầy đủ navigation để POS / StockDocument / InventoryAdjustment dùng ngay.
    /// </summary>
    Task<ProductVariantUnitBarcode?> FindByBarcodeAsync(int storeId, string barcode, CancellationToken ct = default);

    /// <summary>
    /// Kiểm tra barcode đã tồn tại trong store chưa.
    /// </summary>
    Task<bool> ExistsBarcodeAsync(int storeId, string barcode, int? excludeId = null, CancellationToken ct = default);


    /// <summary>
    /// Bỏ trạng thái primary của các barcode khác trong cùng conversion.
    /// Dùng khi set 1 barcode mới thành primary.
    /// </summary>
    Task ClearPrimaryFlagsAsync(int productUnitConversionId, int? excludeId = null, CancellationToken ct = default);


    /// <summary>
    /// Thêm barcode mới.
    /// </summary>
    Task AddAsync(ProductVariantUnitBarcode entity, CancellationToken ct = default);
    Task<ProductVariantUnitBarcode?> FindDuplicateWithDetailsAsync(
    int storeId,
    string barcode,
    int? excludeId = null,
    CancellationToken ct = default);

    /// <summary>
    /// Lưu thay đổi.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);

    Task<ProductVariantUnitBarcode?> GetActiveByBarcodeAsync(
         int storeId,
         string barcode,
         CancellationToken ct = default);

    Task<ProductVariantUnitBarcode?> GetCurrentActiveForConversionAsync(
        int conversionId,
        CancellationToken ct = default);

    Task<ProductVariantUnitBarcode?> GetCurrentActiveForConversionForLookupAsync(
        int storeId,
        int conversionId,
        CancellationToken ct = default);

    Task<ProductUnitConversion?> GetConversionForChangeAsync(
        int conversionId,
        CancellationToken ct = default);

    Task<bool> ExistsActiveBarcodeAsync(
        int storeId,
        string barcode,
        int? excludeBarcodeId = null,
        CancellationToken ct = default);

    Task<List<ProductVariantUnitBarcode>> GetByConversionIdAsync(
    int storeId,
    int conversionId,
    CancellationToken ct = default);

    Task<ProductUnitConversion?> GetConversionDetailAsync(
        int storeId,
        int conversionId,
        CancellationToken ct = default);

    Task<ProductVariantUnitBarcode?> GetPrimaryActiveByConversionIdAsync(
    int productUnitConversionId,
    CancellationToken ct = default);

    Task<ProductVariantUnitBarcode?> GetNearestActiveCandidateAsync(
        int productUnitConversionId,
        int excludeBarcodeId,
        CancellationToken ct = default);

}