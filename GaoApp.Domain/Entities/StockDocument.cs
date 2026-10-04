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
    [StringLength(255)]
    public string? DocumentTitle { get; set; }

    public StockDocumentType Type { get; set; } = StockDocumentType.Receipt;

    public StockDocumentStatus Status { get; set; } = StockDocumentStatus.Draft;

    public DateTime DocumentDate { get; set; } = DateTime.UtcNow;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public PurchaseReceiptSource ReceiptSource { get; set; } = PurchaseReceiptSource.LegacyDirect;

    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }

    // Captured when the receipt is created; never inferred from a later login or approval.
    public int? EntryTerminalId { get; set; }
    [StringLength(150)]
    public string? EntryTerminalName { get; set; }
    [StringLength(30)]
    public string? EntryTerminalCode { get; set; }

    public ReceivingSessionState ReceivingSessionState { get; set; }
        = ReceivingSessionState.None;
    public int? ReceivingOwnerUserId { get; set; }
    public Guid? ReceivingLeaseToken { get; set; }
    public DateTime? ReceivingLeaseExpiresAtUtc { get; set; }
    public DateTime? ReceivingLastSavedAtUtc { get; set; }
    public int ReceivingRevision { get; set; }

    [StringLength(500)]
    public string? DirectReceiptReason { get; set; }

    public bool HasVat { get; set; }

    /// <summary>Null preserves unclassified legacy receipts. Posting and invoice follow-up are independent.</summary>
    public bool? WaitForInputInvoice { get; set; }

    /// <summary>
    /// Whether input VAT is included in the inventory cost posted by this receipt.
    /// The default is false: VAT remains a commercial amount only.
    /// </summary>
    public bool IncludeVatInInventoryCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SubtotalBeforeVat { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VatAmount { get; set; }

    public bool HasFreight { get; set; }

    /// <summary>
    /// Whether freight is allocated to receipt lines and capitalized into inventory.
    /// Freight settlement remains separate regardless of this choice.
    /// </summary>
    public bool CapitalizeFreightInInventoryCost { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal FreightTotal { get; set; }

    [StringLength(250)]
    public string? FreightPayeeName { get; set; }

    [StringLength(1000)]
    public string? FreightNote { get; set; }

    public bool IsFreightPaid { get; set; }

    /// <summary>Tiền hàng của phiếu đã được thanh toán ngay hay còn ghi nhận công nợ.</summary>
    public bool IsMerchandisePaid { get; set; }

    /// <summary>
    /// Tên người/đơn vị nhận tiền khi nhập trực tiếp và không chọn nhà cung cấp danh mục.
    /// </summary>
    [StringLength(250)]
    public string? MerchandisePayeeName { get; set; }

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
    /// Nhân viên đã gửi yêu cầu xin sửa phiếu sau khi đã gửi duyệt.
    /// Phiếu vẫn ở trạng thái PendingApproval, quản lý quyết định có trả về sửa hay không.
    /// </summary>
    public bool HasRevisionRequest { get; set; } = false;

    [StringLength(1000)]
    public string? RevisionRequestNote { get; set; }

    public DateTime? RevisionRequestedAtUtc { get; set; }

    public int? RevisionRequestedByUserId { get; set; }

    public DateTime? RevisionResolvedAtUtc { get; set; }

    public int? RevisionResolvedByUserId { get; set; }

    /// <summary>
    /// Giữ lại để tương thích nghiệp vụ cũ "confirm".
    /// Thực chất lúc approved thì cũng set field này.
    /// </summary>
    public DateTime? ConfirmedAtUtc { get; set; }

    public int? ConfirmedByUserId { get; set; }

    /// <summary>
    /// Immutable receipt owner snapshot written atomically at confirmation.
    /// It is intentionally receipt-scoped; non-receipt stock documents remain unaffected.
    /// </summary>
    public int? ConfirmedLegalEntityId { get; set; }
    public LegalEntity? ConfirmedLegalEntity { get; set; }

    public ICollection<StockDocumentLine> Lines { get; set; }
        = new List<StockDocumentLine>();
    /// <summary>
    /// Danh sách hóa đơn XML đầu vào gắn với phiếu nhập này.
    /// </summary>
    public ICollection<StockDocumentInputInvoiceMap> InputInvoiceMaps { get; set; }
        = new List<StockDocumentInputInvoiceMap>();

    /// <summary>
    /// Danh sách map dòng nhập với dòng XML.
    /// </summary>
    public ICollection<StockDocumentLineInputInvoiceMap> LineInputInvoiceMaps { get; set; }
        = new List<StockDocumentLineInputInvoiceMap>();

    public ICollection<PurchasePayable> PurchasePayables { get; set; }
        = new List<PurchasePayable>();
    public ICollection<PurchaseReceivingAction> ReceivingActions { get; set; }
        = new List<PurchaseReceivingAction>();
    public ICollection<StockDocumentProvisionalItem> ProvisionalItems { get; set; }
        = new List<StockDocumentProvisionalItem>();
}
