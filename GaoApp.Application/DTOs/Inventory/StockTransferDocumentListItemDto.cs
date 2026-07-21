using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockTransferDocumentListItemDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public DateTime DocumentDate { get; set; }

    public int FromWarehouseId { get; set; }
    public string FromWarehouseName { get; set; } = default!;

    public int ToWarehouseId { get; set; }
    public string ToWarehouseName { get; set; } = default!;

    public StockTransferDocumentStatus Status { get; set; }
    public int TotalLines { get; set; }
    public string? Note { get; set; }
}