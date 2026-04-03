using GaoApp.Domain.Enums;

namespace GaoApp.Application.Common.Inventory;

/// <summary>
/// Helper dùng để tính overdue cho OrderInventoryIssue.
/// 
/// Mục tiêu:
/// - gom rule overdue vào 1 chỗ
/// - tránh copy/paste logic ở nhiều service khác nhau
/// - sau này đổi rule chỉ cần sửa tại đây
/// </summary>
public static class InventoryIssueOverdueHelper
{
    /// <summary>
    /// Kiểm tra một status có còn là trạng thái "mở"
    /// và còn nằm trong luồng chờ xử lý hay không.
    /// 
    /// Chỉ các trạng thái đang mở mới có khái niệm overdue.
    /// </summary>
    public static bool IsOpenStatus(InventoryResolutionStatus status)
    {
        return status == InventoryResolutionStatus.PendingResolution
            || status == InventoryResolutionStatus.ReadyForApproval;
    }

    /// <summary>
    /// Kiểm tra issue có overdue hay không.
    /// 
    /// Rule:
    /// - chỉ áp dụng cho status còn mở
    /// - CreatedAtUtc <= nowUtc - 24h
    /// </summary>
    public static bool IsOverdue(
        InventoryResolutionStatus status,
        DateTime createdAtUtc,
        DateTime nowUtc)
    {
        if (!IsOpenStatus(status))
            return false;

        return createdAtUtc <= nowUtc.AddHours(-InventoryIssueConstants.OverdueAfterHours);
    }

    /// <summary>
    /// Tính số giờ đã quá hạn.
    /// 
    /// Ví dụ:
    /// - issue tạo lúc 2026-04-01 08:00 UTC
    /// - rule overdue sau 24h
    /// - thời điểm kiểm tra là 2026-04-02 15:00 UTC
    /// => overdue 7 giờ
    /// 
    /// Nếu chưa overdue thì trả về 0.
    /// </summary>
    public static int GetOverdueHours(
        InventoryResolutionStatus status,
        DateTime createdAtUtc,
        DateTime nowUtc)
    {
        if (!IsOverdue(status, createdAtUtc, nowUtc))
            return 0;

        var overdueSinceUtc = createdAtUtc.AddHours(InventoryIssueConstants.OverdueAfterHours);
        var overdueHours = (int)Math.Floor((nowUtc - overdueSinceUtc).TotalHours);

        return Math.Max(0, overdueHours);
    }

    /// <summary>
    /// Tính tổng số ngày tuổi của issue kể từ lúc tạo đến hiện tại.
    /// Hàm này không bắt buộc cho overdue,
    /// nhưng đôi lúc có ích khi cần hiển thị "đã treo bao lâu".
    /// </summary>
    public static int GetAgeHours(DateTime createdAtUtc, DateTime nowUtc)
    {
        var ageHours = (int)Math.Floor((nowUtc - createdAtUtc).TotalHours);
        return Math.Max(0, ageHours);
    }
}