using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.UserInStores;

public class CreateEmployeeInStoreRequest
{
    [Required(ErrorMessage = "Tên đăng nhập là bắt buộc.")]
    [StringLength(100)]
    public string UserName { get; set; } = default!;

    [Required(ErrorMessage = "Họ tên là bắt buộc.")]
    [StringLength(200)]
    public string FullName { get; set; } = default!;

    [StringLength(200)]
    public string? Email { get; set; }

    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    [Required(ErrorMessage = "Mật khẩu là bắt buộc.")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Mật khẩu tối thiểu 6 ký tự.")]
    public string Password { get; set; } = default!;

    [Required(ErrorMessage = "Vai trò là bắt buộc.")]
    public int RoleId { get; set; }

    [StringLength(100)]
    public string? PositionName { get; set; }

    public DateTime? JoinedDate { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }

    public bool IsActive { get; set; } = true;
}