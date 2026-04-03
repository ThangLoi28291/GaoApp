using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
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

    public List<StockDocumentLineDto> Lines { get; set; } = new();
}