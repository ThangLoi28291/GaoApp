using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Bộ lọc danh sách phiếu điều chỉnh kho.
/// </summary>
public class InventoryAdjustmentDocumentFilterDto
{
    public string? Keyword { get; set; }

    public int? WarehouseId { get; set; }

    public InventoryAdjustmentDocumentStatus? Status { get; set; }

    public InventoryTransactionType? AdjustmentType { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}