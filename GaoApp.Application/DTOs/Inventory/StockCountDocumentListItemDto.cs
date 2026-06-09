using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Dùng cho màn danh sách phiếu kiểm kê.
/// </summary>
public class StockCountDocumentListItemDto
{
    public int Id { get; set; }
    public string DocumentNo { get; set; } = string.Empty;
    public string? DocumentName { get; set; }
    public DateTime DocumentDate { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public StockCountDocumentStatus Status { get; set; }
    public string? Note { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public int TotalLines { get; set; }
}