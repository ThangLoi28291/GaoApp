using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.AdminMenus;

public class SaveAdminMenuItemRequest : GaoApp.Application.DTOs.Security.SecurityActionConfirmation
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

    [StringLength(200)]
    public string? PermissionCode { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    // Rejection-only compatibility fields. Old forms must never create permissions
    // or grant/revoke role access through the menu endpoint.
    public bool? CreateNewPermission { get; set; }

    [StringLength(200)]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? NewPermissionCode { get; set; }

    [StringLength(250)]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? NewPermissionName { get; set; }

    [StringLength(120)]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? NewPermissionGroupName { get; set; }

    public List<int>? AssignRoleIds { get; set; }
}
