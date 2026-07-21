// GaoApp.Application/DTOs/AdminMenus/AdminMenuItemDto.cs
namespace GaoApp.Application.DTOs.AdminMenus;

public class AdminMenuItemDto
{
    public int Id { get; set; }
    public int? ParentId { get; set; }
    public string Title { get; set; } = "";
    public string? Area { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public string? Url { get; set; }
    public string? Icon { get; set; }
    public string? PermissionCode { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public bool IsSystem { get; set; }

    public List<AdminMenuItemDto> Children { get; set; } = new();
}