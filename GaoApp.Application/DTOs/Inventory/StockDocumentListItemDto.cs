using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentListItemDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = default!;
    public DateTime DocumentDate { get; set; }
    public string WarehouseName { get; set; } = default!;
    public string? SupplierName { get; set; }
    public StockDocumentStatus Status { get; set; }
    public decimal TotalAmount { get; set; }

    public DateTime? SubmittedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
}