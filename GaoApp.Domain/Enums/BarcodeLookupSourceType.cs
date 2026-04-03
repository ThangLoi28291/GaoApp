namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguồn barcode dùng để thêm dòng hàng vào POS
/// </summary>
public enum BarcodeLookupSourceType
{
    /// <summary>
    /// Barcode lấy từ bảng ProductVariantUnitBarcode
    /// </summary>
    UnitBarcode = 1,

    /// <summary>
    /// Barcode mặc định / legacy của ProductVariant
    /// </summary>
    VariantBarcode = 2,

    /// <summary>
    /// Barcode cũ trong bảng lịch sử
    /// </summary>
    BarcodeHistory = 3,

    /// <summary>
    /// Không phải scan barcode, mà thêm thủ công từ tìm kiếm/chọn variant
    /// </summary>
    ManualVariant = 4
}