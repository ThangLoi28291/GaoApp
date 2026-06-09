using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

public class ResetPasswordRequest
{
    public int UserId { get; set; }

    [Required]
    [MinLength(6)]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    public string ConfirmPassword { get; set; } = string.Empty;
}