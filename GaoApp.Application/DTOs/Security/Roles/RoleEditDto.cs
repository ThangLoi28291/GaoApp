namespace GaoApp.Application.DTOs.Security.Roles;

public class RoleEditDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public bool IsSystemRole { get; set; }

    public int PermissionCount { get; set; }
    public int UserCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
}