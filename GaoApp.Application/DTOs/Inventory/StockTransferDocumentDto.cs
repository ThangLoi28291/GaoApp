using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockTransferDocumentDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public DateTime DocumentDate { get; set; }

    public int FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = default!;

    public int ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = default!;

    public StockTransferDocumentStatus Status { get; set; }
    public string? Note { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public int? SubmittedByUserId { get; set; }

    public DateTime? ApprovedAtUtc { get; set; }
    public int? ApprovedByUserId { get; set; }

    public DateTime? ConfirmedAtUtc { get; set; }
    public int? ConfirmedByUserId { get; set; }

    public int TotalLines { get; set; }

    public List<StockTransferLineDto> Lines { get; set; } = new();
}