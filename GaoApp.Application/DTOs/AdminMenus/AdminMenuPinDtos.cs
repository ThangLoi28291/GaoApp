using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.AdminMenus;

public sealed record AdminMenuPinChoice(int Id, string Title, string Icon, string Path);
public sealed record AdminMenuPinWorkspace(string Version, IReadOnlyList<int> MenuIds,
    IReadOnlyList<AdminMenuPinChoice> Choices, IReadOnlyList<MenuVisibilitySubject> Subjects);
public sealed record AdminMenuPinPreview(IReadOnlyList<int> VisibleIds);

public sealed class SaveAdminMenuPinsRequest
{
    [Required, StringLength(64, MinimumLength = 64)] public string Version { get; set; } = "";
    [Required, MaxLength(4)] public List<int> MenuIds { get; set; } = [];
}
