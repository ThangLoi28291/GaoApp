using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Account;

/// <summary>
/// ViewModel cho màn hình login.
/// Chỉ nhập username và password.
/// Store và terminal được resolve tự động.
/// </summary>
public class LoginVm
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    public string UserName { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = default!;

    public string? ReturnUrl { get; set; }
}