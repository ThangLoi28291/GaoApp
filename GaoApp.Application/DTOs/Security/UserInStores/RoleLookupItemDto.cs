namespace GaoApp.Application.DTOs.Security.UserInStores;

/// <summary>
/// Item lookup Role để bind dropdown chọn role.
/// </summary>
public class RoleLookupItemDto
{
    public int RoleId { get; set; }

    public string RoleName { get; set; } = string.Empty;

    public string RoleCode { get; set; } = string.Empty;

    public bool IsActive { get; set; }

    public string DisplayText => $"{RoleName} ({RoleCode})";
}