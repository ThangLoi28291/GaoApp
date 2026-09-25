using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

public class ResetPasswordRequest
{
    public int UserId { get; set; }

    [Required]
    [StringLength(128, MinimumLength = 12, ErrorMessage = "Mật khẩu phải có từ 12 đến 128 ký tự.")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}
