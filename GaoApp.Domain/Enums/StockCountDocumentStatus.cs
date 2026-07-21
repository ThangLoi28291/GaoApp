namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái phiếu kiểm kê kho.
/// </summary>
public enum StockCountDocumentStatus
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
    /// Đã duyệt và đã phát sinh movement kiểm kê.
    /// </summary>
    Confirmed = 3,

    /// <summary>
    /// Bị từ chối duyệt.
    /// </summary>
    Rejected = 4,

    /// <summary>
    /// Hủy phiếu.
    /// </summary>
    Cancelled = 5
}