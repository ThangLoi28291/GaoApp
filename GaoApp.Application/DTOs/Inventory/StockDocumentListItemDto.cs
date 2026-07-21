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
    public string? SupplierName { get; set; }
    public StockDocumentStatus Status { get; set; }
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
