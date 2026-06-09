using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.AdminMenus;

public class SaveAdminMenuItemRequest
{
    public int Id { get; set; }

    public int? ParentId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên menu.")]
    [StringLength(120)]
    public string Title { get; set; } = "";

    public string? Area { get; set; } = "Admin";
    public string? Controller { get; set; }
    public string? Action { get; set; } = "Index";
    public string? Url { get; set; }
    public string? Icon { get; set; }

    public string? PermissionCode { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // =====================================================
    // NÂNG CẤP: tạo permission mới ngay khi tạo menu
    // =====================================================

    public bool CreateNewPermission { get; set; }

    [StringLength(200)]
    public string? NewPermissionCode { get; set; }

    [StringLength(250)]
    public string? NewPermissionName { get; set; }

    [StringLength(120)]
    public string? NewPermissionGroupName { get; set; }

    // =====================================================
    // NÂNG CẤP: gán quyền này cho các vai trò
    // =====================================================

    public List<int> AssignRoleIds { get; set; } = new();
}