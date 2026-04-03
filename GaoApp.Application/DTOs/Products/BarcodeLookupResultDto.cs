namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO trả về khi lookup barcode cho POS / Stock / Inventory.
///
/// CHỐT KIẾN TRÚC:
/// - Không dùng ProductVariant.Barcode
/// - Barcode chính thức nằm ở ProductVariantUnitBarcode
/// - Lookup ưu tiên barcode active hiện tại
/// - Nếu không thấy active thì có thể fallback sang barcode history
/// - DTO này vẫn giữ cấu trúc giàu dữ liệu để POS dùng trực tiếp
/// </summary>
public sealed class BarcodeLookupResultDto
{
    /// <summary>
    /// Có tìm thấy barcode hay không.
    /// Giúp API/POS xử lý rõ hơn thay vì phải kiểm tra ProductId = 0.
    /// </summary>
    public bool Found { get; set; }

    /// <summary>
    /// Barcode người dùng vừa scan / nhập vào.
    /// Đây là mã đầu vào thực tế.
    /// </summary>
    public string InputBarcode { get; set; } = string.Empty;

    /// <summary>
    /// Barcode đã match trong quá trình lookup.
    /// - Nếu là mã active: thường = InputBarcode
    /// - Nếu là mã cũ: vẫn là mã người dùng scan vào
    /// </summary>
    public string MatchedBarcode { get; set; } = string.Empty;

    /// <summary>
    /// Barcode active hiện tại của conversion.
    /// - Nếu scan barcode hiện tại: thường = Barcode
    /// - Nếu scan barcode cũ: field này là mã active mới nhất
    /// </summary>
    public string? CurrentActiveBarcode { get; set; }

    /// <summary>
    /// Barcode record active đang được resolve cuối cùng.
    /// Dùng để trace/log sâu hơn nếu cần.
    /// </summary>
    public int? BarcodeRecordId { get; set; }

    /// <summary>
    /// Có phải barcode hiện tại đang active hay không.
    /// </summary>
    public bool IsCurrentBarcode { get; set; }

    /// <summary>
    /// Có phải lookup này match từ barcode lịch sử hay không.
    /// </summary>
    public bool IsHistoricalBarcode { get; set; }

    /// <summary>
    /// Thông điệp cảnh báo cho UI/POS.
    /// Ví dụ:
    /// - Đây là mã cũ, hệ thống đã đổi sang mã mới ...
    /// </summary>
    public string? WarningMessage { get; set; }

    /// <summary>
    /// Id sản phẩm cha.
    /// </summary>
    public int ProductId { get; set; }

    /// <summary>
    /// Tên sản phẩm cha.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>
    /// Id của ProductVariant.
    /// </summary>
    public int ProductVariantId { get; set; }

    /// <summary>
    /// SKU của variant.
    /// </summary>
    public string? VariantSku { get; set; }

    /// <summary>
    /// Id của ProductUnitConversion đã match.
    /// Đây là field rất quan trọng để POS / Stock / Adjustment biết
    /// barcode này đang trỏ về đơn vị nào.
    /// </summary>
    public int? ProductUnitConversionId { get; set; }

    /// <summary>
    /// Id đơn vị hiện tại mà barcode đang trỏ tới.
    /// </summary>
    public int UnitId { get; set; }

    /// <summary>
    /// Tên đơn vị hiện tại.
    /// </summary>
    public string? UnitName { get; set; }

    /// <summary>
    /// Đơn vị gốc của sản phẩm cha.
    /// </summary>
    public int BaseUnitId { get; set; }

    /// <summary>
    /// Tên đơn vị gốc.
    /// </summary>
    public string? BaseUnitName { get; set; }

    /// <summary>
    /// Hệ số quy đổi về đơn vị gốc.
    /// Ví dụ:
    /// - lon = 1
    /// - lốc = 6
    /// - thùng = 24
    /// </summary>
    public decimal Factor { get; set; } = 1m;

    /// <summary>
    /// Có phải đơn vị gốc hay không.
    /// </summary>
    public bool IsBaseUnit { get; set; }

    /// <summary>
    /// Có phải đơn vị mặc định để bán hay không.
    /// </summary>
    public bool IsDefaultForSale { get; set; }

    /// <summary>
    /// Giá vốn hiện tại của variant.
    /// </summary>
    public decimal CostPrice { get; set; }

    /// <summary>
    /// Giá bán resolve ra tại thời điểm lookup.
    /// Ưu tiên:
    /// - ProductUnitConversion.Price
    /// - ProductVariant.Price
    /// - Product.BasePrice
    /// </summary>
    public decimal SellPrice { get; set; }

    /// <summary>
    /// Barcode được resolve cuối cùng cho record active hiện tại.
    /// Thường nên là barcode active đang dùng.
    /// </summary>
    public string Barcode { get; set; } = string.Empty;

    /// <summary>
    /// Nguồn dữ liệu match.
    /// Giá trị gợi ý:
    /// - UnitBarcode
    /// - BarcodeHistory
    /// </summary>
    public string SourceType { get; set; } = string.Empty;
}