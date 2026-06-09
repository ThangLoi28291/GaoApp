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
    /// IP nội bộ cũ của máy POS.
    /// OBSOLETE: Không còn dùng để nhận diện terminal.
    /// Chỉ giữ lại để tương thích dữ liệu cũ.
    /// </summary>
    [StringLength(100)]
    public string? LocalIp { get; set; }

    /// <summary>
    /// Tên máy tính / thiết bị nếu muốn lưu thêm để dễ quản trị.
    /// </summary>
    [StringLength(150)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// OBSOLETE: Không còn dùng. Terminal hiện nhận diện bằng POSTerminalDevice.DeviceKey.
    /// </summary>
    public bool AutoResolveByIp { get; set; } = false;

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
    /// <summary>
    /// Danh sách thiết bị đã ghép với terminal này.
    /// </summary>
    public ICollection<POSTerminalDevice> Devices { get; set; } = new List<POSTerminalDevice>();
}