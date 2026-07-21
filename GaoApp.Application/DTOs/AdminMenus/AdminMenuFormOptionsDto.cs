namespace GaoApp.Application.DTOs.AdminMenus;

public class AdminMenuFormOptionsDto
{
    public List<AdminMenuItemDto> ParentMenus { get; set; } = new();

    public List<AdminMenuPermissionOptionDto> Permissions { get; set; } = new();

    public List<AdminMenuRoleOptionDto> Roles { get; set; } = new();
}

public class AdminMenuPermissionOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string? Name { get; set; }
    public string? GroupName { get; set; }
}

public class AdminMenuRoleOptionDto
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = "";
    public string RoleName { get; set; } = "";
}