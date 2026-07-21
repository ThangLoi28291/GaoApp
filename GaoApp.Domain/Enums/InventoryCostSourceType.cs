namespace GaoApp.Domain.Enums;

/// <summary>
/// Nguồn gốc của đơn giá vốn được dùng tại thời điểm ghi nhận.
/// Enum này rất quan trọng vì sau này khi tra cứu/revalue,
/// ta biết cost hiện tại đang đến từ đâu.
/// </summary>
public enum InventoryCostSourceType
{
    /// <summary>
    /// Không xác định / chưa gán rõ nguồn.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Giá vốn lấy từ moving average hiện tại của tồn kho.
    /// Đây là nguồn chuẩn khi tồn đủ và valuation đã ổn định.
    /// </summary>
    MovingAverage = 1,

    /// <summary>
    /// Giá vốn tạm lấy từ lần nhập gần nhất.
    /// Dùng khi moving average chưa đủ tin cậy hoặc đang âm kho.
    /// </summary>
    LastInboundCost = 2,

    /// <summary>
    /// Giá vốn tạm lấy từ giá vốn mặc định của variant/product.
    /// Thường là fallback thấp ưu tiên hơn.
    /// </summary>
    DefaultProductCost = 3,

    /// <summary>
    /// Giá vốn do người dùng / nghiệp vụ chỉ định thủ công.
    /// </summary>
    Manual = 4,

    /// <summary>
    /// Giá trị được sinh bởi bút toán revaluation adjustment.
    /// </summary>
    RevaluationAdjustment = 5
}