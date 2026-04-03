namespace GaoApp.Domain.Enums;

/// <summary>
/// Loại nghiệp vụ phát sinh với barcode.
/// </summary>
public enum BarcodeHistoryActionType
{
    /// <summary>
    /// Gán barcode mới lần đầu.
    /// </summary>
    Assigned = 1,

    /// <summary>
    /// Đổi barcode cũ sang barcode mới.
    /// </summary>
    Replaced = 2,

    /// <summary>
    /// Ngưng sử dụng barcode.
    /// </summary>
    Deactivated = 3,

    /// <summary>
    /// kích hoạt lại mã cũ (nếu cho phép)
    /// </summary>

    Reactivated = 4,
    /// <summary>
    /// Dữ liệu import từ hệ thống cũ hoặc đồng bộ ngoài.
    /// </summary>
    Imported = 5
}