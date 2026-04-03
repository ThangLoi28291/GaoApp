namespace GaoApp.Application.Common.Inventory;

/// <summary>
/// Formatter chuyên để build text hiển thị cho UI liên quan tới issue tồn kho.
/// 
/// Mục tiêu:
/// - tránh lặp logic format text ở nhiều nơi
/// - UI list/detail/dashboard hiển thị thống nhất
/// </summary>
public static class InventoryIssueUiFormatter
{
    /// <summary>
    /// Build text overdue cho UI.
    /// 
    /// Ví dụ:
    /// - overdueHours = 5  => "Quá hạn 5 giờ"
    /// - overdueHours = 24 => "Quá hạn 1 ngày"
    /// - overdueHours = 27 => "Quá hạn 1 ngày 3 giờ"
    /// </summary>
    public static string? BuildOverdueText(bool isOverdue, int overdueHours)
    {
        if (!isOverdue)
            return null;

        if (overdueHours < 24)
            return $"Quá hạn {overdueHours} giờ";

        var days = overdueHours / 24;
        var remainHours = overdueHours % 24;

        if (remainHours == 0)
            return $"Quá hạn {days} ngày";

        return $"Quá hạn {days} ngày {remainHours} giờ";
    }

    /// <summary>
    /// Build text tuổi của issue nếu sau này bạn muốn dùng thêm.
    /// Ví dụ:
    /// - 10 giờ => "10 giờ"
    /// - 50 giờ => "2 ngày 2 giờ"
    /// </summary>
    public static string BuildAgeText(int ageHours)
    {
        if (ageHours < 24)
            return $"{ageHours} giờ";

        var days = ageHours / 24;
        var remainHours = ageHours % 24;

        if (remainHours == 0)
            return $"{days} ngày";

        return $"{days} ngày {remainHours} giờ";
    }
}