namespace GaoApp.Domain.Enums;

/// <summary>
/// Kiểu xác thực khi gọi nhà cung cấp hóa đơn điện tử.
/// </summary>
public enum InvoiceProviderAuthMode
{
    /// <summary>
    /// Gọi /auth/login lấy access_token.
    /// Tài khoản hiện tại của bạn không dùng hướng này.
    /// </summary>
    TokenLogin = 1,

    /// <summary>
    /// Gọi API trực tiếp bằng Authorization Basic.
    /// Đây là hướng đúng với tài khoản Viettel test hiện tại.
    /// </summary>
    BasicAuth = 2
}