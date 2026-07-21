using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseOrders")]
public sealed class PurchaseOrder : BaseStoreEntity, IAuditTrackedEntity
{
    [Required, StringLength(50)]
    public string OrderNumber { get; set; } = string.Empty;

    /// <summary>
    /// Null với đơn tạo trực tiếp; có giá trị khi đơn được sinh từ yêu cầu mua.
    /// </summary>
    public int? SourcePurchaseRequestId { get; set; }
    public PurchaseRequest? SourcePurchaseRequest { get; set; }

    [StringLength(64)]
    public string? SourceConversionKey { get; set; }

    [StringLength(250)]
    public string? Title { get; set; }

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int ExpectedWarehouseId { get; set; }
    public Warehouse ExpectedWarehouse { get; set; } = null!;

    public int LegalEntityId { get; set; }
    public LegalEntity LegalEntity { get; set; } = null!;

    public DateTime OrderDate { get; set; } = DateTime.UtcNow;
    public DateTime? ExpectedDeliveryDate { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }

    /// <summary>
    /// Lý do nghiệp vụ khi đơn được lập trực tiếp, không đi từ yêu cầu mua hàng.
    /// Null đối với đơn có SourcePurchaseRequestId và các bản ghi cũ.
    /// </summary>
    [StringLength(500)]
    public string? OutsideRequestReason { get; set; }

    public bool HasVat { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubtotalBeforeVat { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VatTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAfterVat { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
    public int? RejectedByUserId { get; set; }
    public DateTime? ReturnedAtUtc { get; set; }
    public int? ReturnedByUserId { get; set; }
    public DateTime? SentToSupplierAtUtc { get; set; }
    public int? SentToSupplierByUserId { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }

    [StringLength(1000)]
    public string? WorkflowNote { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();
    public ICollection<PurchaseOrderAction> Actions { get; set; } = new List<PurchaseOrderAction>();
    public ICollection<StockDocument> Receipts { get; set; } = new List<StockDocument>();
    public ICollection<PurchaseRequestAction> PurchaseRequestActions { get; set; } = new List<PurchaseRequestAction>();
}
