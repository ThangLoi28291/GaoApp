namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại chứng từ / hồ sơ liên quan đến action trong timeline của case tồn âm.
/// Lưu ý:
/// - Giữ nguyên các giá trị cũ để không làm lệch dữ liệu đã lưu.
/// - Thêm Order để workflow POS finalize có thể tham chiếu đúng về đơn bán gốc.
/// </summary>
public enum InventoryIssueReferenceType
{
    /// <summary>
    /// Không có tham chiếu cụ thể.
    /// </summary>
    None = 0,

    /// <summary>
    /// Phiếu nhập hàng / chứng từ nhập kho.
    /// </summary>
    GoodsReceipt = 1,

    /// <summary>
    /// Phiếu điều chỉnh tồn kho.
    /// </summary>
    InventoryAdjustment = 2,

    /// <summary>
    /// Phiếu trả hàng / hoàn hàng.
    /// </summary>
    ReturnOrder = 3,

    /// <summary>
    /// Chứng từ kiểm kho.
    /// </summary>
    StockCount = 4,

    /// <summary>
    /// Chứng từ revaluation / điều chỉnh giá vốn.
    /// </summary>
    CostRevaluation = 5,

    /// <summary>
    /// Đơn bán gốc phát sinh case tồn âm.
    /// Dùng cho action như:
    /// - CaseCreated
    /// - NegativeDetected
    /// - Reopened
    /// </summary>
    Order = 6,

    /// <summary>
    /// Tham chiếu khác.
    /// </summary>
    Other = 99
}