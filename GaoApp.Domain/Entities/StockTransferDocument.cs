using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Header phiếu chuyển kho.
/// Chỉ khi Confirmed mới sinh InventoryTransaction.
/// </summary>
[Table("StockTransferDocument")]
public class StockTransferDocument : BaseStoreEntity
{
    /// <summary>
    /// Số phiếu chuyển kho.
    /// Ví dụ: CK-20260315-0001
    /// </summary>
    [Required]
    [StringLength(50)]
    public string DocumentNo { get; set; } = default!;

    /// <summary>
    /// Ngày chứng từ nghiệp vụ.
    /// </summary>
    public DateTime DocumentDate { get; set; }

    /// <summary>
    /// Kho nguồn.
    /// </summary>
    public int FromWarehouseId { get; set; }

    /// <summary>
    /// Kho đích.
    /// </summary>
    public int ToWarehouseId { get; set; }

    /// <summary>
    /// Trạng thái phiếu.
    /// </summary>
    public StockTransferDocumentStatus Status { get; set; } = StockTransferDocumentStatus.Draft;

    /// <summary>
    /// Ghi chú chung.
    /// </summary>
    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Thời điểm submit.
    /// </summary>
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>
    /// User submit.
    /// Hiện tại để int? cho đồng bộ với codebase đang có.
    /// </summary>
    public int? SubmittedByUserId { get; set; }

    /// <summary>
    /// Thời điểm reject/approve trung gian.
    /// Giữ sẵn để mở rộng sau.
    /// </summary>
    public DateTime? ApprovedAtUtc { get; set; }

    /// <summary>
    /// User reject/approve trung gian.
    /// </summary>
    public int? ApprovedByUserId { get; set; }

    /// <summary>
    /// Thời điểm confirm chính thức.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    /// <summary>
    /// User confirm.
    /// </summary>
    public int? ConfirmedByUserId { get; set; }

    public Warehouse FromWarehouse { get; set; } = default!;
    public Warehouse ToWarehouse { get; set; } = default!;

    public ICollection<StockTransferLine> Lines { get; set; } = new List<StockTransferLine>();
}