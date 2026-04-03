using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Điều kiện lọc thẻ kho / ledger kho.
/// Dùng cho màn hình tra cứu lịch sử phát sinh tồn kho.
/// </summary>
public class InventoryLedgerQueryRequest
{
    /// <summary>
    /// Lọc theo kho.
    /// Null hoặc <= 0 thì lấy tất cả kho.
    /// </summary>
    public int? WarehouseId { get; set; }

    /// <summary>
    /// Lọc theo biến thể sản phẩm.
    /// Null hoặc <= 0 thì lấy tất cả variant.
    /// </summary>
    public int? ProductVariantId { get; set; }

    /// <summary>
    /// Từ khóa tìm kiếm theo:
    /// - tên sản phẩm
    /// - SKU
    /// - barcode variant
    /// - mã tham chiếu chứng từ
    /// - ghi chú giao dịch
    /// </summary>
    public string? Keyword { get; set; }

    /// <summary>
    /// Lọc theo loại giao dịch kho.
    /// Ví dụ:
    /// - Import
    /// - AdjustmentIncrease
    /// - AdjustmentDecrease
    /// - StockCountGain
    /// - StockCountLoss
    /// - TransferOut
    /// - TransferIn
    /// </summary>
    public InventoryTransactionType? TransactionType { get; set; }

    /// <summary>
    /// Lọc theo loại chứng từ tham chiếu.
    /// Ví dụ:
    /// - StockDocument
    /// - StockCount
    /// - StockTransfer
    /// - Order
    /// </summary>
    public InventoryReferenceType? ReferenceType { get; set; }

    /// <summary>
    /// Lọc theo mã / id chứng từ tham chiếu.
    /// Ví dụ:
    /// - PNK240315001
    /// - KK240315002
    /// - CK240315003
    /// </summary>
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Lọc từ ngày (local date).
    /// Thường được quy đổi về đầu ngày khi query.
    /// </summary>
    public DateTime? FromDate { get; set; }

    /// <summary>
    /// Lọc đến ngày (local date).
    /// Thường được quy đổi về cuối khoảng ngày
    /// bằng cách lấy nhỏ hơn ngày kế tiếp.
    /// </summary>
    public DateTime? ToDate { get; set; }

    /// <summary>
    /// Chỉ lấy các giao dịch mà sau giao dịch tồn bị âm.
    /// Hữu ích để soi lỗi phát sinh âm kho.
    /// </summary>
    public bool OnlyNegativeAfterTransaction { get; set; }

    /// <summary>
    /// Cột sắp xếp.
    /// Gợi ý hỗ trợ:
    /// - TransactionDate
    /// - ProductName
    /// - Sku
    /// - Warehouse
    /// - TransactionType
    /// - ReferenceType
    /// - ReferenceId
    /// - AfterQuantity
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
    /// Trang hiện tại. Mặc định = 1.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Số dòng mỗi trang. Mặc định = 20.
    /// </summary>
    public int PageSize { get; set; } = 20;
}