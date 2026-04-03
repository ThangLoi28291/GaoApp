using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Đầu chứng từ kho.
/// Phase 5.4 hiện tại dùng cho phiếu nhập kho.
/// </summary>
[Table("StockDocument")]
public class StockDocument : BaseStoreEntity, IAuditTrackedEntity
{
    [Required]
    [StringLength(50)]
    public string DocumentNo { get; set; } = default!;

    public StockDocumentType Type { get; set; } = StockDocumentType.Receipt;

    public StockDocumentStatus Status { get; set; } = StockDocumentStatus.Draft;

    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; } = 0;

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Thời điểm gửi duyệt.
    /// </summary>
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>
    /// Người gửi duyệt.
    /// Giai đoạn sau mới lấy user thật.
    /// </summary>
    public int? SubmittedByUserId { get; set; }

    /// <summary>
    /// Thời điểm duyệt phiếu.
    /// </summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// Người duyệt phiếu.
    /// Giai đoạn sau mới lấy user thật.
    /// </summary>
    public int? ApprovedByUserId { get; set; }

    /// <summary>
    /// Ghi chú duyệt / từ chối duyệt.
    /// </summary>
    [StringLength(1000)]
    public string? ApprovalNote { get; set; }

    /// <summary>
    /// Giữ lại để tương thích nghiệp vụ cũ "confirm".
    /// Thực chất lúc approved thì cũng set field này.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    public int? ConfirmedByUserId { get; set; }

    public ICollection<StockDocumentLine> Lines { get; set; }
        = new List<StockDocumentLine>();
}