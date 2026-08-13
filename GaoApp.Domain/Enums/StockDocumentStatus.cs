namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái chứng từ kho.
/// </summary>
public enum StockDocumentStatus
{
    /// <summary>
    /// Đang nhập liệu.
    /// </summary>
    Draft = 1,

    /// <summary>
    /// Đã gửi chờ duyệt.
    /// </summary>
    PendingApproval = 2,

    /// <summary>
    /// Đã duyệt và đã phát sinh giao dịch kho.
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// Đã được trả về để chỉnh sửa.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// Hủy phiếu.
    /// </summary>
    Cancelled = 5
}
