using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Mapping User vào Store.
/// 
/// - 1 User có thể thuộc nhiều Store
/// - mỗi Store user có thể có Role khác nhau
/// </summary>
[Table("UserInStores")]
public class UserInStore : BaseStoreEntity
{
    public int UserId { get; set; }

    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;

    public User User { get; set; } = default!;

    public Role Role { get; set; } = default!;
}