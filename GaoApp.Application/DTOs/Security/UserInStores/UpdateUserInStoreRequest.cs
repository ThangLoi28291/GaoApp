using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Request cập nhật mapping user-store-role.
/// </summary>
public class UpdateUserInStoreRequest : GaoApp.Application.DTOs.Security.SecurityActionConfirmation
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public int RoleId { get; set; }

    public bool IsActive { get; set; }
    public bool UpdateProfile { get; set; }

    [StringLength(200, ErrorMessage = "Họ tên tối đa 200 ký tự.")]
    public string? FullName { get; set; }

    [StringLength(200, ErrorMessage = "Email tối đa 200 ký tự.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    [StringLength(100)]
    public string? PositionName { get; set; }

    public DateTime? JoinedDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
