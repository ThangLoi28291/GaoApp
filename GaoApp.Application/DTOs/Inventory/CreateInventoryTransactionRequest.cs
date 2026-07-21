using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

public class CreateInventoryTransactionRequest
{
    public int WarehouseId { get; set; }
    public int ProductVariantId { get; set; }

    public InventoryTransactionType TransactionType { get; set; }
    public InventoryReferenceType ReferenceType { get; set; }
    public string? ReferenceId { get; set; }

    /// <summary>
    /// Dương = tăng tồn, âm = giảm tồn
    /// </summary>
    public decimal QuantityChange { get; set; }

    public string? Note { get; set; }
    public DateTime? OccurredAtUtc { get; set; }
}