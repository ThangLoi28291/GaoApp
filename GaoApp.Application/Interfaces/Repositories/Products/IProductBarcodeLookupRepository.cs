using GaoApp.Application.DTOs.Products;

namespace GaoApp.Application.Interfaces.Repositories.Products;

/// <summary>
/// Repository lookup barcode cho sản phẩm.
///
/// CHỐT KIẾN TRÚC:
/// - Không còn ProductVariant.Barcode
/// - Mọi barcode chính thức đều nằm ở ProductVariantUnitBarcode
/// - Vì vậy chỉ còn lookup theo unit barcode
/// </summary>
public interface IProductBarcodeLookupRepository
{
    /// <summary>
    /// Tìm sản phẩm theo barcode của ProductVariantUnitBarcode.
    /// Trả về đầy đủ thông tin:
    /// - Product
    /// - ProductVariant
    /// - ProductUnitConversion
    /// - Unit hiện tại
    /// - Base unit
    /// - Giá bán resolve
    /// </summary>
    Task<BarcodeLookupResultDto?> FindByUnitBarcodeAsync(
        string barcode,
        CancellationToken ct = default);
}