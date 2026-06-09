namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// Request dùng cho luồng tạo conversion nội bộ của hệ thống.
///
/// Khác với DTO từ UI:
/// - DTO này phục vụ application/service
/// - rõ ràng hơn cho flow tạo conversion mặc định
/// </summary>
public class CreateProductUnitConversionRequest
{
    /// <summary>
    /// Store hiện tại.
    /// </summary>
    public int StoreId { get; set; }

    /// <summary>
    /// Variant cha của conversion.
    /// </summary>
    public int ProductVariantId { get; set; }

    /// <summary>
    /// Đơn vị của conversion.
    /// </summary>
    public int UnitId { get; set; }

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// Base unit luôn là 1.
    /// </summary>
    public decimal Factor { get; set; } = 1m;

    /// <summary>
    /// Có phải đơn vị gốc hay không.
    /// </summary>
    public bool IsBaseUnit { get; set; }

    /// <summary>
    /// Có phải đơn vị bán mặc định hay không.
    /// </summary>
    public bool IsDefaultForSale { get; set; }

    /// <summary>
    /// Giá bán riêng của conversion.
    /// Nếu null thì fallback về Variant.Price / Product.BasePrice.
    /// </summary>
    public decimal? Price { get; set; }

    /// <summary>
    /// Giá sỉ riêng cho đơn vị quy đổi: lon/lốc/thùng.
    /// Null hoặc <= 0 nghĩa là dùng giá lẻ.
    /// </summary>
    public decimal? WholesalePrice { get; set; }
    /// <summary>
    /// Trạng thái hoạt động.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Thứ tự hiển thị.
    /// </summary>
    public int SortOrder { get; set; } = 0;

    /// <summary>
    /// Có tự sinh barcode primary nội bộ hay không.
    /// </summary>
    public bool AutoGeneratePrimaryBarcode { get; set; } = true;

    /// <summary>
    /// Ghi chú cho barcode tự sinh.
    /// </summary>
    public string? BarcodeNote { get; set; }
}