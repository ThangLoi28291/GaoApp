namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại sequence dùng để cấp số chứng từ.
/// Tách riêng để sau này mở rộng:
/// - phiếu nhập
/// - phiếu chuyển kho
/// - phiếu kiểm kê
/// - trả hàng
/// </summary>
public enum DocumentNumberSequenceType
{
    /// <summary>
    /// Phiếu nhập kho.
    /// Format hiện tại: NK-yyyyMMdd-0001
    /// </summary>
    StockReceipt = 1,

    /// <summary>
    /// Phiếu chuyển kho.
    /// </summary>
    StockTransfer = 2,

    /// <summary>
    /// Phiếu kiểm kê.
    /// </summary>
    StockCount = 3,

    /// <summary>
    /// Đơn đặt hàng mua.
    /// </summary>
    PurchaseOrder = 4,

    /// <summary>
    /// Yêu cầu mua nội bộ.
    /// </summary>
    PurchaseRequest = 5
}
