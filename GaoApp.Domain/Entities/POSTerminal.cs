using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Đại diện cho một máy POS / terminal / quầy thu ngân cụ thể trong 1 store.
/// 
/// Thiết kế:
/// - Mỗi terminal thuộc đúng 1 store.
/// - Có thể auto nhận diện terminal theo LocalIp.
/// - 1 terminal chỉ nên có tối đa 1 ca đang mở tại 1 thời điểm.
/// </summary>
[Table("POSTerminals")]
public class POSTerminal : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Mã terminal duy nhất trong 1 store.
    /// Ví dụ: POS01, POS02.
    /// </summary>
    [Required]
    [StringLength(30)]
    public string Code { get; set; } = default!;

    /// <summary>
    /// Tên hiển thị terminal.
    /// Ví dụ: Quầy thu ngân 1.
    /// </summary>
    [Required]
    [StringLength(150)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// IP nội bộ tĩnh của máy POS.
    /// Dùng để tự động nhận diện terminal lúc đăng nhập.
    /// Ví dụ: 192.168.1.20
    /// </summary>
    [StringLength(100)]
    public string? LocalIp { get; set; }

    /// <summary>
    /// Tên máy tính / thiết bị nếu muốn lưu thêm để dễ quản trị.
    /// </summary>
    [StringLength(150)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Có cho phép tự động bind terminal theo IP hay không.
    /// </summary>
    public bool AutoResolveByIp { get; set; } = true;

    /// <summary>
    /// Trạng thái terminal.
    /// </summary>
    public POSTerminalStatus Status { get; set; } = POSTerminalStatus.Active;

    /// <summary>
    /// Có còn hoạt động hay không.
    /// </summary>
    public bool IsActive { get; set; } = true;

    [StringLength(300)]
    public string? Description { get; set; }

    /// <summary>
    /// Danh sách ca đã mở trên terminal này.
    /// </summary>
    public ICollection<POSShift> Shifts { get; set; } = new List<POSShift>();
}