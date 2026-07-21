using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.Roles;

/// <summary>
/// Dữ liệu tạo mới Role cho store hiện tại.
/// StoreId không bind từ client để đảm bảo multi-tenant an toàn.
/// </summary>
public class RoleCreateRequest
{
    [Required(ErrorMessage = "Tên vai trò là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Tên vai trò không được vượt quá 100 ký tự.")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Mã vai trò là bắt buộc.")]
    [StringLength(100, ErrorMessage = "Mã vai trò không được vượt quá 100 ký tự.")]
    public string Code { get; set; } = string.Empty;


    /// <summary>
    /// Trạng thái hoạt động.
    /// </summary>
    public bool IsActive { get; set; } = true;
}