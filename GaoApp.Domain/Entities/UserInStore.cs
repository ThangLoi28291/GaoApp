using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Mapping User vào Store.
/// 
/// - 1 User có thể thuộc nhiều Store
/// - mỗi Store user có thể có Role khác nhau
/// - đồng thời lưu thêm thông tin nhân sự theo từng Store
/// </summary>
[Table("UserInStores")]
public class UserInStore : BaseStoreEntity
{
    public int UserId { get; set; }

    public int RoleId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Số điện thoại nhân viên tại cửa hàng.
    /// </summary>
    [StringLength(30)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Chức vụ hiển thị nội bộ: Thu ngân, Kho, Trưởng ca...
    /// </summary>
    [StringLength(100)]
    public string? PositionName { get; set; }

    /// <summary>
    /// Ngày bắt đầu làm việc tại cửa hàng.
    /// </summary>
    public DateTime? JoinedDate { get; set; }

    /// <summary>
    /// Ghi chú nội bộ về nhân viên.
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }

    public User User { get; set; } = default!;

    public Role Role { get; set; } = default!;
}