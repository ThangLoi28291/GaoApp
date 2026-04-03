using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GaoApp.Domain.Entities;

[Table("Permissions")]
public class Permission
{
    public int Id { get; set; }

    [Required, StringLength(150)]
    public string Code { get; set; } = default!;

    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    [Required, StringLength(100)]
    public string GroupName { get; set; } = default!;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}