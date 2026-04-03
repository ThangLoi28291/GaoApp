using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO lịch sử điều chỉnh kho.
/// </summary>
public class StockAdjustmentHistoryItemDto
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    public int ProductVariantId { get; set; }
    public string ProductVariantName { get; set; } = string.Empty;

    public InventoryTransactionType TransactionType { get; set; }

    public decimal QuantityChange { get; set; }
    public decimal BeforeQty { get; set; }
    public decimal AfterQty { get; set; }

    public string? ReferenceId { get; set; }
    public string? Note { get; set; }

    public DateTime OccurredAtUtc { get; set; }
}