namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO 1 dòng variant dùng cho màn hình create/edit sản phẩm.
///
/// CHỐT KIẾN TRÚC:
/// - ProductVariant không còn Barcode
/// - Barcode được quản lý riêng ở ProductUnitConversion + ProductVariantUnitBarcode
/// - DTO này giữ dữ liệu chính của 1 variant:
///   SKU, tên hiển thị variant, giá, trạng thái, ảnh, thuộc tính...
/// </summary>
public class ProductVariantRowDto
{
    /// <summary>
    /// Id của variant.
    /// Null khi là dòng variant mới tạo trên UI.
    /// </summary>
    public int? Id { get; set; }

    /// <summary>
    /// SKU của variant.
    /// Vẫn giữ ở cấp variant vì đây là mã quản trị nội bộ của biến thể.
    /// </summary>
    public string Sku { get; set; } = "";

    /// <summary>
    /// Tên hiển thị theo từng variant.
    /// Ví dụ:
    /// sữa tươi th size l vị cam hương bưởi
    /// 
    /// - Khi tạo variant: hệ thống có thể tự sinh.
    /// - Khi edit ở bảng variant: người dùng có thể sửa trực tiếp.
    /// - POS / autocomplete / order line sẽ ưu tiên dùng tên này để hiển thị.
    /// </summary>
    public string? ProductVariantName { get; set; }

    /// <summary>
    /// Giá vốn hiện tại của variant.
    /// </summary>
    public decimal CostPrice { get; set; } = 0;

    /// <summary>
    /// Giá bán mặc định của variant.
    /// Nếu đơn vị bán cụ thể có giá riêng thì ProductUnitConversion.Price sẽ override.
    /// </summary>
    public decimal? Price { get; set; }
    /// <summary>
    /// Giá sỉ theo đơn vị gốc của variant.
    /// Null hoặc <= 0 nghĩa là chưa cấu hình giá sỉ.
    /// </summary>
    public decimal? WholesalePrice { get; set; }
    /// <summary>
    /// Giá bán lẻ của đơn vị gốc.
    /// Lấy từ ProductUnitConversion.IsBaseUnit = true.
    /// Chỉ dùng để hiển thị ngoài bảng variant.
    /// </summary>
    public decimal? BaseUnitPrice { get; set; }

    /// <summary>
    /// Giá sỉ của đơn vị gốc.
    /// Lấy từ ProductUnitConversion.IsBaseUnit = true.
    /// Chỉ dùng để hiển thị ngoài bảng variant.
    /// </summary>
    public decimal? BaseUnitWholesalePrice { get; set; }

    /// <summary>
    /// Tên đơn vị gốc để hiển thị.
    /// Ví dụ: cái, hộp, lon.
    /// </summary>
    public string? BaseUnitName { get; set; }

    /// <summary>
    /// Tổng số đơn vị quy đổi hiện có (đang hoạt động hoặc không), kể cả đơn vị gốc.
    /// Dùng cho nhanh: hiển thị tiến độ "đang có đơn vị hay chưa".
    /// </summary>
    public int UnitConversionCount { get; set; } = 0;

    /// <summary>
    /// Tổng số barcode đang hoạt động hiện có trên toàn bộ các đơn vị quy đổi.
    /// </summary>
    public int ActiveBarcodeCount { get; set; } = 0;

    /// <summary>
    /// Variant có đang hoạt động hay không.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Ảnh đại diện riêng của variant.
    /// Có thể null để fallback ảnh của product.
    /// </summary>
    public int? PrimaryProductImageId { get; set; }

    /// <summary>
    /// Danh sách AttributeValueId gắn với variant này.
    /// Ví dụ:
    /// - Màu đỏ
    /// - Size L
    /// </summary>
    public List<int> AttributeValueIds { get; set; } = new();

    /// <summary>
    /// POS: nếu variant đã phát sinh giao dịch thì khóa một số thao tác nhạy cảm
    /// như đổi tổ hợp thuộc tính, đổi SKU mạnh tay hoặc xóa.
    /// </summary>
    public bool IsLocked { get; set; } = false;
    /// <summary>
    /// Cờ hóa đơn đầu vào.
    /// true: dòng bán của variant này sẽ được đưa vào InvoiceDetail bán ra.
    /// false: vẫn bán POS bình thường nhưng không đưa vào InvoiceDetail.
    /// </summary>
    public bool HasInputInvoice { get; set; } = false;
}
