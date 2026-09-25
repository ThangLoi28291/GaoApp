using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Chuyển động lịch sử bổ sung cho sổ tồn hóa đơn đầu vào.
/// Đây là nguồn chứng từ độc lập với InventoryTransaction/InventoryBalance.
/// </summary>
public sealed class InvoiceInputStockSupplementalMovement : BaseStoreEntity
{
    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;

    public DateTime EffectiveAtUtc { get; set; }

    public decimal QuantityChange { get; set; }

    public InvoiceInputStockSupplementalMovementType MovementType { get; set; }

    public string LegacySourceKey { get; set; } = default!;

    // Source identifiers only: never foreign keys to GaoApp orders/invoices.
    public long? LegacyOrderId { get; set; }
    public string? LegacyInvoiceNumber { get; set; }
    public string? LegacyInvoiceSymbol { get; set; }

    public string? SourcePeriod { get; set; }

    public string? Note { get; set; }
}
