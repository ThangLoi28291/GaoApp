namespace GaoApp.Domain.Enums;

/// <summary>
/// Trạng thái xử lý hậu kiểm tồn kho của đơn đã bán.
/// Tách biệt hoàn toàn với OrderStatus.
/// </summary>
public enum InventoryResolutionStatus
{
    /// <summary>
    /// Không có issue tồn kho.
    /// </summary>
    None = 0,

    /// <summary>
    /// Đã phát sinh âm kho, đang chờ xử lý.
    /// </summary>
    PendingResolution = 1,

    /// <summary>
    /// Đã có xử lý nghiệp vụ, chờ quản lý duyệt.
    /// </summary>
    ReadyForApproval = 2,

    /// <summary>
    /// Quản lý đã duyệt đóng case.
    /// </summary>
    Approved = 3,

    /// <summary>
    /// Quản lý từ chối, yêu cầu xử lý lại.
    /// </summary>
    Rejected = 4
}