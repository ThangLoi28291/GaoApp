namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại chứng từ nguồn phát sinh giao dịch kho.
/// Dùng để truy vết InventoryTransaction đến từ nghiệp vụ nào.
/// </summary>
public enum InventoryReferenceType
{
    /// <summary>
    /// Không có chứng từ nguồn.
    /// </summary>
    None = 0,

    /// <summary>
    /// Đơn bán hàng / POS order.
    /// </summary>
    Order = 1,

    /// <summary>
    /// Phiếu nhập hàng / chứng từ mua hàng.
    /// </summary>
    PurchaseReceipt = 2,

    /// <summary>
    /// Trả hàng nhà cung cấp.
    /// </summary>
    PurchaseReturn = 3,

    /// <summary>
    /// Hoàn tiền / trả hàng từ đơn bán.
    /// </summary>
    Refund = 4,

    /// <summary>
    /// Điều chỉnh tồn thủ công.
    /// </summary>
    Adjustment = 5,

    /// <summary>
    /// Chuyển kho.
    /// </summary>
    StockTransfer = 6,

    /// <summary>
    /// Kiểm kê kho.
    /// </summary>
    StockCount = 7,

    /// <summary>
    /// Giữ hàng / reserve hàng.
    /// </summary>
    Hold = 8,

        /// <summary>
        /// Chứng từ kho: StockDocument
        /// </summary>
    StockDocument = 10
}