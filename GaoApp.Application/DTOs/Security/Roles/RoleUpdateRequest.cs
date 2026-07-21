using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.Roles;

/// <summary>
/// Dữ liệu cập nhật Role.
/// </summary>
public class RoleUpdateRequest
{
    [Required]
    public int Id { get; set; }

    [Required(ErrorMessage = "Tên vai trò là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Tên vai trò không được vượt quá 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã vai trò là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Mã vai trò không được vượt quá 100 ký tự.")]
    public string Code { get; set; } = string.Empty;



    public bool IsActive { get; set; }
}