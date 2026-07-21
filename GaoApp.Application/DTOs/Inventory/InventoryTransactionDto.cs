using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class InventoryTransactionDto
{
    public int Id { get; set; }
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = null!;

    public int ProductVariantId { get; set; }
    public string ProductVariantName { get; set; } = null!;

    public InventoryTransactionType TransactionType { get; set; }
    public InventoryReferenceType ReferenceType { get; set; }
    public string? ReferenceId { get; set; }

    public decimal QuantityChange { get; set; }
    public decimal BeforeQty { get; set; }
    public decimal AfterQty { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}
