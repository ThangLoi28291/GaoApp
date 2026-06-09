using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Phiếu điều chỉnh kho.
/// Lưu ý:
/// - Tạo phiếu KHÔNG tác động tồn kho.
/// - Chỉ khi Approved mới tạo InventoryTransaction.
/// </summary>
[Table("InventoryAdjustmentDocuments")]
public class InventoryAdjustmentDocument : BaseStoreEntity, IAuditTrackedEntity
{
    [Required]
    [StringLength(50)]
    public string DocumentNo { get; set; } = default!;

    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    /// <summary>
    /// Tăng kho hoặc giảm kho.
    /// Dùng lại enum InventoryTransactionType hiện có:
    /// - AdjustmentIncrease
    /// - AdjustmentDecrease
    /// </summary>
    public InventoryTransactionType AdjustmentType { get; set; }

    public InventoryAdjustmentDocumentStatus Status { get; set; }
        = InventoryAdjustmentDocumentStatus.Draft;

    public InventoryAdjustmentReasonType ReasonType { get; set; }
        = InventoryAdjustmentReasonType.Other;

    [StringLength(1000)]
    public string? Note { get; set; }

    [StringLength(1000)]
    public string? ApprovalNote { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }

    public DateTime? RejectedAtUtc { get; set; }
    public int? RejectedByUserId { get; set; }

    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }

    public ICollection<InventoryAdjustmentLine> Lines { get; set; }
        = new List<InventoryAdjustmentLine>();
}