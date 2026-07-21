// GaoApp.Domain/Entities/AdminMenuItem.cs
using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public class AdminMenuItem : BaseStoreEntity
{
    public int? ParentId { get; set; }
    public AdminMenuItem? Parent { get; set; }
    public ICollection<AdminMenuItem> Children { get; set; } = new List<AdminMenuItem>();

    [Required, StringLength(120)]
    public string Title { get; set; } = default!;

    [StringLength(80)]
    public string? Area { get; set; } = "Admin";

    [StringLength(120)]
    public string? Controller { get; set; }

    [StringLength(120)]
    public string? Action { get; set; } = "Index";

    [StringLength(300)]
    public string? Url { get; set; }

    [StringLength(120)]
    public string? Icon { get; set; }

    [StringLength(200)]
    public string? PermissionCode { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsSystem { get; set; }
}