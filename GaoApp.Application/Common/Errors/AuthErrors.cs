using GaoApp.Application.Common.Results;

namespace GaoApp.Application.Common.Errors;

/// <summary>
/// Các kết quả đăng nhập dự kiến, có mã ổn định và thông điệp an toàn để hiển thị.
/// Lỗi hạ tầng, database hoặc framework không thuộc danh sách này và phải throw.
/// </summary>
public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("Auth.InvalidCredentials", "Tên đăng nhập hoặc mật khẩu không đúng.");

    public static readonly Error AccountInactive =
        new("Auth.AccountInactive", "Tài khoản đang bị khóa hoặc ngừng hoạt động.");

    public static readonly Error StoreAccessDenied =
        new("Auth.StoreAccessDenied", "Tài khoản không được phép truy cập cửa hàng này.");

    public static readonly Error RoleUnavailable =
        new("Auth.RoleUnavailable", "Vai trò của tài khoản không còn hợp lệ.");

    public static readonly Error TerminalSelectionRequired =
        new(
            "Auth.TerminalSelectionRequired",
            "Thiết bị này chưa được ghép POS. Vui lòng chọn máy POS để tiếp tục.");

    public static readonly Error DevicePairingFailed =
        new(
            "Auth.DevicePairingFailed",
            "Không thể ghép thiết bị POS. Vui lòng thử lại.");
}
