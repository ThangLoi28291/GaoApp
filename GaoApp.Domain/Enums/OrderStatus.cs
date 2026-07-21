namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái xử lý của đơn hàng
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// Đơn đang ở chế độ nháp (POS đang bán)
    /// Có thể thêm/xóa sản phẩm, thêm/xóa thanh toán
    /// </summary>
    Draft = 0,

    /// <summary>
    /// Đơn đang chế độ giữ để tính khách khác
    /// </summary>
    OnHold = 1,

    /// <summary>
    /// Đơn đã hoàn tất (đã chốt hóa đơn)
    /// Không cho chỉnh sửa nữa
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Đơn đã bị hủy khi còn ở trạng thái nháp / giữ
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// Đã hủy sau khi chốt đơn
    /// Dùng cho các lỗi thao tác tức thời tại quầy
    /// </summary>
    Voided = 4,

    /// <summary>
    /// Đơn đã được trả hàng / hoàn tiền
    /// Dùng cho nghiệp vụ sau bán
    /// </summary>
    Refunded = 5
}