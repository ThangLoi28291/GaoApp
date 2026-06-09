using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public string? DocumentTitle { get; set; }
    public StockDocumentType Type { get; set; }
    public StockDocumentStatus Status { get; set; }

    public DateTime DocumentDate { get; set; }

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = default!;

    public int? SupplierId { get; set; }
    public string? SupplierName { get; set; }

    public string? Note { get; set; }

    public decimal TotalAmount { get; set; }

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