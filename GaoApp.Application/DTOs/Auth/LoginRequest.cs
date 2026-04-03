using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Auth;

/// <summary>
/// Request đăng nhập.
/// Store và Terminal không nhập tay nữa.
/// Store lấy từ tenant.
/// Terminal lấy từ IP máy.
/// </summary>
public class LoginRequest
{
    [Required]
    public string UserName { get; set; } = default!;

    [Required]
    public string Password { get; set; } = default!;

    public string? ReturnUrl { get; set; }
}