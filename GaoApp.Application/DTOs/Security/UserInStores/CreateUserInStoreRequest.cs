using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Request tạo mới UserInStore cho store hiện tại.
/// </summary>
public class CreateUserInStoreRequest
{
    [Required(ErrorMessage = "Người dùng là bắt buộc.")]
    public int UserId { get; set; }

    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;
}