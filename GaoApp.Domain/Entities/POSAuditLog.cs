using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("POSAuditLogs")]
public class POSAuditLog : BaseStoreEntity
{
    /// <summary>
    /// Hành động
    /// Ví dụ:
    /// ORDER_FINALIZED
    /// ORDER_VOIDED
    /// ORDER_REFUNDED
    /// ORDER_CANCELLED
    /// PAYMENT_ADDED
    /// </summary>
    [Required]
    [StringLength(50)]
    public string Action { get; set; } = default!;

    /// <summary>
    /// Id đơn hàng liên quan (nếu có)
    /// </summary>
    public int? OrderId { get; set; }

    /// <summary>
    /// Id user thực hiện
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Ghi chú
    /// </summary>
    [StringLength(500)]
    public string? Note { get; set; }

    /// <summary>
    /// JSON metadata
    /// </summary>
    public string? MetadataJson { get; set; }
}