using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("AuditLogs")]
public class AuditLog
{
    public long Id { get; set; }

    /// <summary>
    /// Store thực hiện hành động.
    /// GaoApp là multi-store nên audit log vận hành luôn phải gắn StoreId.
    /// Chỉ một số log hệ thống đặc biệt mới không có store.
    /// </summary>
    [Required]
    public int StoreId { get; set; }

    /// <summary>
    /// User thực hiện hành động.
    /// Có thể null nếu là background job hoặc system action.
    /// </summary>
    public int? ActorUserId { get; set; }

    [StringLength(200)]
    public string? ActorUserName { get; set; }

    /// <summary>
    /// Module nghiệp vụ
    /// Ví dụ:
    /// Authentication
    /// Orders
    /// Inventory
    /// Products
    /// Users
    /// </summary>
    public AuditModuleType Module { get; set; }

    /// <summary>
    /// Loại hành động
    /// Ví dụ:
    /// Create / Update / Delete / Finalize / Refund
    /// </summary>
    public AuditActionType ActionType { get; set; }

    /// <summary>
    /// Entity bị tác động
    /// Ví dụ:
    /// Order
    /// ProductVariant
    /// User
    /// </summary>
    [StringLength(150)]
    public string? EntityName { get; set; }

    /// <summary>
    /// Id entity
    /// dạng string để hỗ trợ int / guid / composite key
    /// </summary>
    [StringLength(100)]
    public string? EntityId { get; set; }

    /// <summary>
    /// Tên hiển thị dễ đọc
    /// Ví dụ:
    /// "Đơn hàng POS-0000123"
    /// </summary>
    [StringLength(300)]
    public string? EntityDisplay { get; set; }

    /// <summary>
    /// Mô tả ngắn gọn hiển thị trên grid
    /// </summary>
    [StringLength(1000)]
    public string? Summary { get; set; }

    /// <summary>
    /// JSON giá trị cũ
    /// </summary>
    public string? OldValuesJson { get; set; }

    /// <summary>
    /// JSON giá trị mới
    /// </summary>
    public string? NewValuesJson { get; set; }

    /// <summary>
    /// JSON danh sách cột thay đổi
    /// </summary>
    public string? ChangedColumnsJson { get; set; }

    /// <summary>
    /// Trace id của request
    /// giúp trace request chain
    /// </summary>
    [StringLength(100)]
    public string? TraceId { get; set; }

    [StringLength(100)]
    public string? IpAddress { get; set; }

    [StringLength(2000)]
    public string? UserAgent { get; set; }

    [StringLength(500)]
    public string? Path { get; set; }

    /// <summary>
    /// Audit thành công hay thất bại
    /// </summary>
    public bool IsSuccess { get; set; } = true;

    [StringLength(2000)]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Thời điểm ghi log
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}