namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Điều kiện lọc danh sách tồn kho hiện tại.
/// Dùng cho màn hình tra cứu tồn kho.
/// </summary>
public class InventoryBalanceQueryRequest
{
    /// <summary>
    /// Lọc theo kho.
    /// Null hoặc <= 0 nghĩa là lấy tất cả kho.
    /// </summary>
    public int? WarehouseId { get; set; }

    /// <summary>
    /// Lọc theo biến thể sản phẩm.
    /// Null hoặc <= 0 nghĩa là lấy tất cả variant.
    /// </summary>
    public int? ProductVariantId { get; set; }

    /// <summary>
    /// Từ khóa tìm kiếm theo:
    /// - tên sản phẩm
    /// - SKU
    /// - barcode variant
    /// - barcode đơn vị (nếu query hỗ trợ)
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Chỉ lấy các dòng đang âm kho.
    /// Nếu = true thì chỉ lấy các dòng có OnHandQty < 0.
    /// </summary>
    public bool OnlyNegative { get; set; }

    /// <summary>
    /// Chỉ lấy các dòng đang còn tồn dương.
    /// Nếu = true thì chỉ lấy các dòng có OnHandQty > 0.
    /// </summary>
    public bool OnlyPositive { get; set; }

    /// <summary>
    /// Cột sắp xếp.
    /// Gợi ý hỗ trợ:
    /// - ProductName
    /// - Sku
    /// - Warehouse
    /// - OnHandQty
    /// - ReservedQty
    /// - AvailableQty
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Chiều sắp xếp:
    /// - asc
    /// - desc
    /// Nếu null thì service/repository nên tự gán mặc định.
    /// </summary>
    public string? SortDirection { get; set; }

    /// <summary>
    /// Trang hiện tại.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số dòng mỗi trang.
    /// </summary>
    public int PageSize { get; set; } = 20;
}