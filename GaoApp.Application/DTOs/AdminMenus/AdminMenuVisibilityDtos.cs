using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.AdminMenus;

public sealed record MenuVisibilitySubject(string Type, int Id, string Name, string Detail, int MemberCount, int PersonalCount, bool IsPersonal);
public sealed record MenuVisibilityNode(int Id, int? ParentId, string Title, string Icon, bool Available, string? UnavailableReason, bool IsFolder);
public sealed record MenuVisibilityWorkspace(string Type, int Id, int RoleId, string Name, string Detail, bool Inherited,
    int MemberCount, int PersonalCount, string Version, List<int> HiddenIds, List<int> InheritedHiddenIds, List<MenuVisibilityNode> Menus);

public sealed class SaveMenuVisibilityRequest
{
    [Required, RegularExpression("^(role|employee)$")]
    public string Type { get; set; } = "";
    [Range(1, int.MaxValue)] public int Id { get; set; }
    [Required, StringLength(64, MinimumLength = 64)] public string Version { get; set; } = "";
    [Required, MaxLength(2000)] public List<int> HiddenIds { get; set; } = [];
    public bool Reset { get; set; }
}
