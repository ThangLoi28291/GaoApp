using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public string? DocumentTitle { get; set; }
    public StockDocumentType Type { get; set; }
    public StockDocumentStatus Status { get; set; }
    public string RowVersion { get; set; } = string.Empty;

    public DateTime DocumentDate { get; set; }

    public int LegalEntityId { get; set; }
    public string LegalEntityName { get; set; } = default!;

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = default!;

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    public string? Note { get; set; }

    public decimal TotalAmount { get; set; }
    public PurchaseReceiptSource ReceiptSource { get; set; }
    public int? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? DirectReceiptReason { get; set; }
    public bool HasVat { get; set; }
    public bool IncludeVatInInventoryCost { get; set; }
    public decimal SubtotalBeforeVat { get; set; }
    public decimal VatAmount { get; set; }
    public bool HasFreight { get; set; }
    public bool CapitalizeFreightInInventoryCost { get; set; }
    public decimal FreightTotal { get; set; }
    public string? FreightPayeeName { get; set; }
    public string? FreightNote { get; set; }
    public bool IsFreightPaid { get; set; }
    public bool IsMerchandisePaid { get; set; }
    public string? MerchandisePayeeName { get; set; }
    public decimal FreightAllocatedTotal => Lines.Sum(x => x.FreightAllocation);
    public decimal FreightAllocationDifference => FreightTotal - FreightAllocatedTotal;

    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }

    public string? ApprovalNote { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }

    public bool CanEditHeader { get; set; }
    public bool CanEditLines { get; set; }
    public bool HasRevisionRequest { get; set; }
    public string? RevisionRequestNote { get; set; }
    public DateTime? RevisionRequestedAtUtc { get; set; }
    public int? RevisionRequestedByUserId { get; set; }
    public DateTime? RevisionResolvedAtUtc { get; set; }
    public int? RevisionResolvedByUserId { get; set; }

    public List<StockDocumentLineDto> Lines { get; set; } = new();
}
