namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại bút toán valuation.
/// Tách riêng khỏi InventoryTransactionType vì:
/// - 1 transaction có thể sinh 1 hoặc nhiều valuation entry
/// - valuation phục vụ cost layer / revaluation / audit giá trị
/// </summary>
public enum InventoryValuationEntryType
{
    /// <summary>
    /// Bút toán nhập làm tăng giá trị tồn kho.
    /// Ví dụ: nhập hàng, trả hàng khách nhập lại kho, tăng kiểm kê...
    /// </summary>
    Inbound = 1,

    /// <summary>
    /// Bút toán xuất làm giảm giá trị tồn kho.
    /// Ví dụ: bán hàng, xuất chuyển kho, giảm kiểm kê...
    /// </summary>
    Outbound = 2,

    /// <summary>
    /// Bút toán điều chỉnh giá trị không làm đổi số lượng.
    /// Đây là loại cực kỳ quan trọng cho provisional -> final cost.
    /// Quantity thường = 0, Amount có thể +/-.
    /// </summary>
    Revaluation = 3,

    /// <summary>
    /// Khởi tạo giá trị đầu kỳ.
    /// </summary>
    Opening = 4
}