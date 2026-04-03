using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Security.Permissions;

/// <summary>
/// Request lưu danh sách permission được chọn cho một role.
/// </summary>
public class SaveRolePermissionsRequest
{
    [Required]
    public int RoleId { get; set; }

    /// <summary>
    /// Danh sách PermissionId được tick chọn sau cùng.
    /// </summary>
    public List<int> SelectedPermissionIds { get; set; } = new();
}