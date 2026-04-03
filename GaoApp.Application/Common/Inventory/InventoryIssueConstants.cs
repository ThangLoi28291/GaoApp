namespace GaoApp.Application.Common.Inventory;

/// <summary>
/// Hằng số dùng chung cho flow xử lý issue tồn kho.
/// Tách riêng để tránh hard-code ở nhiều nơi.
/// </summary>
public static class InventoryIssueConstants
{
    /// <summary>
    /// Số giờ tối đa cho phép một issue pending tồn tại
    /// trước khi bị xem là quá hạn.
    /// 
    /// Rule hiện tại:
    /// > 1 ngày => overdue
    /// => dùng mốc 24 giờ
    /// </summary>
    public const int OverdueAfterHours = 24;
}