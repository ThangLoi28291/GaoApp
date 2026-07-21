using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Source of truth cho phần hàng/doanh thu của một OrderLine được xuất bởi
/// LegalEntity và Warehouse nào. Order vẫn là đơn gộp duy nhất của POS.
/// </summary>
[Table("OrderLegalEntityAllocations")]
public sealed class OrderLegalEntityAllocation : BaseStoreEntity
{
    public int OrderId { get; set; }
    public Order Order { get; set; } = default!;

    public int OrderLineId { get; set; }
    public OrderLine OrderLine { get; set; } = default!;

    public int ProductVariantId { get; set; }
    public ProductVariant ProductVariant { get; set; } = default!;

    public int? ProductUnitConversionId { get; set; }
    public ProductUnitConversion? ProductUnitConversion { get; set; }

    public int LegalEntityId { get; set; }
    public LegalEntity LegalEntity { get; set; } = default!;

    public int WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = default!;

    /// <summary>
    /// Movement xuất kho được tạo cho đúng fragment allocation này.
    /// Phase void/refund sau phải đảo theo link này, không suy đoán lại kho nguồn.
    /// </summary>
    public int? InventoryTransactionId { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }

    /// <summary>
    /// Snapshot priority tại thời điểm finalize để audit không phụ thuộc cấu hình sau này.
    /// </summary>
    public int SalePriority { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal BaseQuantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Phần tiền dòng sau giảm giá dòng/promotion, trước combo/order/voucher.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal DiscountAllocated { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PromotionDiscountAllocated { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ComboDiscountAllocated { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal OrderDiscountAllocated { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal VoucherDiscountAllocated { get; set; }

    /// <summary>
    /// Phần doanh thu cuối cùng của allocation. Tổng NetAmount của một Order
    /// bắt buộc bằng Order.GrandTotal để không nhân đôi doanh thu.
    /// </summary>
    [Column(TypeName = "decimal(18,2)")]
    public decimal NetAmount { get; set; }

    public OrderLegalEntityAllocationSource AllocationSource { get; set; }

    [StringLength(500)]
    public string? Note { get; set; }
}
