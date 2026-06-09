namespace GaoApp.Application.Common.Exceptions.Pos;

/// <summary>
/// Nhóm loại lỗi chuẩn cho POS.
/// 
/// Dùng để:
/// - Middleware map sang HTTP status code
/// - Frontend quyết định nên hiện toast / modal / banner
/// - Log / thống kê lỗi production
/// 
/// Lưu ý:
/// Dùng string constant thay vì enum để:
/// - serialize JSON dễ
/// - frontend JS đọc trực tiếp thuận tiện
/// - ít phụ thuộc hơn khi mở rộng sau này
/// </summary>
public static class PosErrorTypes
{
    /// <summary>
    /// Lỗi validation đầu vào.
    /// Ví dụ:
    /// - chưa chọn kho
    /// - số tiền <= 0
    /// - lý do để trống
    /// </summary>
    public const string Validation = "validation";

    /// <summary>
    /// Lỗi nghiệp vụ.
    /// Ví dụ:
    /// - chưa mở ca
    /// - còn đơn giữ
    /// - còn giỏ chưa xử lý
    /// </summary>
    public const string BusinessRule = "business_rule";

    /// <summary>
    /// Lỗi ownership / sở hữu ca / terminal.
    /// Ví dụ:
    /// - ca thuộc người khác
    /// - terminal đang do người khác mở ca
    /// </summary>
    public const string Ownership = "ownership";

    /// <summary>
    /// Lỗi thiếu context POS / terminal / user.
    /// Ví dụ:
    /// - không resolve được terminal
    /// - không resolve được user
    /// </summary>
    public const string Context = "context";

    /// <summary>
    /// Lỗi quyền hạn.
    /// Ví dụ:
    /// - không có quyền mở ca
    /// - không có quyền takeover terminal
    /// </summary>
    public const string Permission = "permission";

    /// <summary>
    /// Lỗi xác thực / phiên đăng nhập.
    /// Ví dụ:
    /// - 401
    /// - session hết hạn
    /// </summary>
    public const string Authentication = "authentication";

    /// <summary>
    /// Lỗi xung đột trạng thái.
    /// Ví dụ:
    /// - ca đã đổi ở máy khác
    /// - dữ liệu stale, cần refresh
    /// </summary>
    public const string StateConflict = "state_conflict";

    /// <summary>
    /// Lỗi kỹ thuật / lỗi không mong muốn.
    /// Ví dụ:
    /// - lỗi hệ thống
    /// - lỗi hạ tầng
    /// - exception chưa chuẩn hóa
    /// </summary>
    public const string Technical = "technical";
}