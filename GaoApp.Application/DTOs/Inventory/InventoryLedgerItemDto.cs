using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// 1 dòng ledger / thẻ kho.
/// </summary>
public class InventoryLedgerItemDto
{
    public int Id { get; set; }

    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;

    public int ProductVariantId { get; set; }
    public string ProductVariantName { get; set; } = string.Empty;

    public string? Sku { get; set; }
    public string? Barcode { get; set; }

    public InventoryTransactionType TransactionType { get; set; }
    public string TransactionTypeName { get; set; } = string.Empty;

    public InventoryReferenceType ReferenceType { get; set; }
    public string ReferenceTypeName { get; set; } = string.Empty;

    public string? ReferenceId { get; set; }
    public int? ReferenceLineId { get; set; }

    public decimal QuantityChange { get; set; }
    public decimal BeforeQty { get; set; }
    public decimal AfterQty { get; set; }

    public bool IsNegativeAfterTransaction { get; set; }

    public DateTime OccurredAtUtc { get; set; }
    public string? Note { get; set; }
}