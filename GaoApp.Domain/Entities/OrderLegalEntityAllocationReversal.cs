using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Sổ đảo của từng fragment phân bổ LegalEntity gốc.
/// Lưu cả return có nhập kho, return không nhập kho và void để những lần xử lý sau
/// không được dùng lại cùng một phần hàng/nguồn pháp lý.
/// </summary>
[Table("OrderLegalEntityAllocationReversals")]
public sealed class OrderLegalEntityAllocationReversal : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public int OrderLineId { get; set; }
    public OrderLine OrderLine { get; set; } = default!;

    public int OrderLegalEntityAllocationId { get; set; }
    public OrderLegalEntityAllocation OrderLegalEntityAllocation { get; set; } = default!;

    public int? SalesReturnId { get; set; }
    public SalesReturn? SalesReturn { get; set; }

    public int? SalesReturnLineId { get; set; }
    public SalesReturnLine? SalesReturnLine { get; set; }

    public int LegalEntityId { get; set; }
    public LegalEntity LegalEntity { get; set; } = default!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;

    /// <summary>
    /// Fragment valuation outbound gốc bị đảo/tiêu thụ quyền trả.
    /// Bắt buộc cả với NoRestock để lần trả sau không dùng lại nguồn này.
    /// </summary>
    public int SourceValuationEntryId { get; set; }
    public InventoryValuationEntry SourceValuationEntry { get; set; } = default!;

    /// <summary>
    /// Movement nhập lại kho. Null với ReturnNoRestock.
    /// </summary>
    public int? InventoryTransactionId { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }

    public OrderLegalEntityReversalType ReversalType { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BaseQuantity { get; set; }

    /// <summary>
    /// Phần tiền bị đảo/hoàn gắn với fragment này. Tổng theo chứng từ phải khớp
    /// số tiền unified order/return tương ứng.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal FinancialAmount { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    [StringLength(500)]
    public string? Note { get; set; }
}
