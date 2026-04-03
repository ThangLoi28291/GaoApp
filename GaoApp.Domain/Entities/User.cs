using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Tài khoản người dùng toàn hệ thống.
/// 
/// Lưu ý:
/// - User là global (không có StoreId)
/// - Quyền truy cập store nào được xác định qua bảng UserInStore.
/// </summary>
[Table("Users")]
public class User : BaseEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Tên đăng nhập duy nhất toàn hệ thống.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string UserName { get; set; } = default!;

    /// <summary>
    /// Họ tên hiển thị.
    /// </summary>
    [StringLength(200)]
    public string? FullName { get; set; }

    /// <summary>
    /// Email liên hệ.
    /// </summary>
    [StringLength(200)]
    public string? Email { get; set; }

    /// <summary>
    /// Mật khẩu đã hash.
    /// </summary>
    [Required]
    [StringLength(500)]
    public string PasswordHash { get; set; } = default!;

    /// <summary>
    /// User còn hoạt động ở mức hệ thống hay không.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Dự phòng cho host admin toàn hệ thống.
    /// </summary>
    public bool IsHostAdmin { get; set; } = false;

    /// <summary>
    /// Mapping user vào các store.
    /// </summary>
    public ICollection<UserInStore> UserInStores { get; set; } = new List<UserInStore>();
    public virtual ICollection<OrderInventoryIssue> ApprovedInventoryIssues { get; set; } = new List<OrderInventoryIssue>();
    public virtual ICollection<OrderInventoryIssue> RejectedInventoryIssues { get; set; } = new List<OrderInventoryIssue>();
    public virtual ICollection<OrderInventoryIssueAction> InventoryIssueActions { get; set; } = new List<OrderInventoryIssueAction>();
}