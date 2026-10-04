using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentListItemDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public DateTime DocumentDate { get; set; }
    public int LegalEntityId { get; set; }
    public string LegalEntityName { get; set; } = default!;
    public string WarehouseName { get; set; } = default!;
    public string? CreatedByName { get; set; }
    public string? EntryTerminalName { get; set; }
    public string? EntryTerminalCode { get; set; }
    public string? SupplierName { get; set; }
    public int? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public string? PurchaseOrderTitle { get; set; }
    public StockDocumentStatus Status { get; set; }
    public bool? WaitForInputInvoice { get; set; }
    public bool HasLinkedInputInvoice { get; set; }
    public InputInvoiceReconciliationState? InvoiceReconciliationState { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public int? InvoiceMapId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? InvoiceEvidenceFingerprint { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public string? InvoiceReviewEvidenceJson { get; set; }
    public string InvoiceFollowUp => ReceiptInvoiceFollowUp.Resolve(WaitForInputInvoice, HasLinkedInputInvoice, InvoiceReconciliationState,
        Status == StockDocumentStatus.Confirmed && ReceiptInvoiceFollowUp.IsReviewCurrent(InvoiceReviewEvidenceJson, InvoiceMapId, InvoiceEvidenceFingerprint));
    public int InvoiceWaitingDays => InvoiceFollowUp == "Waiting" && ApprovedAtUtc.HasValue
        ? Math.Max(0, (DateTime.UtcNow.Date - ApprovedAtUtc.Value.Date).Days) : 0;
    public decimal TotalAmount { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public string? DocumentTitle { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int TotalProductTypes { get; set; }
    public int TotalLines { get; set; }
    public bool HasRevisionRequest { get; set; }
    public string? RevisionRequestNote { get; set; }
    public DateTime? RevisionRequestedAtUtc { get; set; }
}
