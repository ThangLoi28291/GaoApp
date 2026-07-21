using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Thiết bị đã ghép với một máy/quầy POS.
/// 
/// Mục tiêu:
/// - Không nhận diện POS bằng IP nữa.
/// - Nhận diện bằng Cookie DeviceKey.
/// - Một POSTerminal có thể có nhiều thiết bị.
/// </summary>
[Table("POSTerminalDevices")]
public class POSTerminalDevice : BaseStoreEntity, IAuditTrackedEntity
{
    /// <summary>
    /// Máy/quầy POS mà thiết bị này được ghép vào.
    /// </summary>
    public int TerminalId { get; set; }

    public POSTerminal Terminal { get; set; } = default!;

    /// <summary>
    /// Khóa nhận diện thiết bị, lưu ở Cookie POS_DEVICE_KEY.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string DeviceKey { get; set; } = default!;

    /// <summary>
    /// Tên thiết bị do người dùng nhập.
    /// Ví dụ: PC thu ngân, iPad quầy 1.
    /// </summary>
    [StringLength(150)]
    public string? DeviceName { get; set; }

    /// <summary>
    /// Trình duyệt / thiết bị truy cập.
    /// </summary>
    [StringLength(500)]
    public string? UserAgent { get; set; }

    /// <summary>
    /// IP cuối cùng truy cập, chỉ để theo dõi, không dùng định danh.
    /// </summary>
    [StringLength(100)]
    public string? LastIp { get; set; }

    public DateTime? LastSeenAtUtc { get; set; }

    public bool IsActive { get; set; } = true;
}