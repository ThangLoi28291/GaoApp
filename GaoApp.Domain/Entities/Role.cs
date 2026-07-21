using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Roles")]
public class Role : BaseStoreEntity
{
    [Required, StringLength(100)]
    public string Code { get; set; } = default!;

    [Required, StringLength(150)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// Role hệ thống mặc định (ADMIN, MANAGER, CASHIER, WAREHOUSE).
    /// Cho phép seed và khóa logic chỉnh sửa nếu cần.
    /// </summary>
    public bool IsSystemRole { get; set; } = false;

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<UserInStore> UserInStores { get; set; } = new List<UserInStore>();
}