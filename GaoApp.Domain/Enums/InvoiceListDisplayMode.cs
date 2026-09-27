

namespace GaoApp.Domain.Enums;
/// <summary>
/// Chế độ lọc danh sách hóa đơn bán ra.
/// Dùng cho màn hình Admin/Invoice/Index.
/// </summary>
public enum InvoiceListDisplayMode
{
    /// <summary>
    /// Hiển thị tất cả.
    /// Mặc định vẫn ưu tiên hóa đơn có dòng/số lượng lên trước.
    /// </summary>
    All = 0,

    /// <summary>
    /// Chỉ hiển thị hóa đơn có dòng hàng, có số lượng.
    /// </summary>
    HasQuantity = 1,

    /// <summary>
    /// Chỉ hiển thị hóa đơn rỗng, chưa có dòng hàng.
    /// </summary>
    Empty = 2,

    /// <summary>
    /// Chỉ hiển thị hóa đơn đã khóa.
    /// </summary>
    Locked = 3,

    /// <summary>
    /// Chỉ hiển thị hóa đơn chưa khóa.
    /// </summary>
    Unlocked = 4,

    /// <summary>
    /// Chỉ hiển thị hóa đơn có dòng tự sinh từ POS OrderLine.
    /// </summary>
    HasAutoLines = 5,

    /// <summary>
    /// Chỉ hiển thị hóa đơn có dòng thêm tay.
    /// </summary>
    HasManualLines = 6,

    /// <summary>Hóa đơn thủ công an toàn, còn thiếu thông tin người mua.</summary>
    ManualWaitingInfo = 7,

    /// <summary>Hóa đơn thủ công đủ thông tin để chuẩn bị phát hành.</summary>
    ManualReady = 8,

    /// <summary>Hóa đơn thủ công cần kiểm tra trạng thái, lỗi hoặc dòng hàng.</summary>
    ManualNeedsAttention = 9
}
