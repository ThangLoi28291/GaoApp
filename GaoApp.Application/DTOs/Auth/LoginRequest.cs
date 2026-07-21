using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Auth;

/// <summary>
/// Request đăng nhập.
/// Store lấy từ tenant.
/// Terminal lấy theo DeviceKey cookie.
/// Nếu chưa có DeviceKey thì cho chọn terminal để ghép thiết bị.
/// </summary>
public class LoginRequest
{
    [Required]
    public string UserName { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Cookie POS_DEVICE_KEY từ trình duyệt.
    /// </summary>
    public string? DeviceKey { get; set; }

    /// <summary>
    /// Terminal được chọn khi thiết bị chưa ghép.
    /// </summary>
    public int? SelectedTerminalId { get; set; }

    /// <summary>
    /// Tên thiết bị người dùng nhập.
    /// </summary>
    public string? DeviceName { get; set; }
    public string? UserAgent { get; set; }
}