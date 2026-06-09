using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Dòng chi tiết phiếu điều chỉnh kho.
/// Quantity là số lượng người dùng nhập theo đơn vị chọn.
/// BaseQuantity là số lượng đã quy đổi về đơn vị gốc.
/// </summary>
[Table("InventoryAdjustmentLines")]
public class InventoryAdjustmentLine : BaseStoreEntity, IAuditTrackedEntity
{
    public int InventoryAdjustmentDocumentId { get; set; }

    public InventoryAdjustmentDocument Document { get; set; } = default!;

    public int ProductVariantId { get; set; }

    public ProductVariant ProductVariant { get; set; } = default!;

    public int? UnitId { get; set; }

    public Unit? Unit { get; set; }

    public int? ProductUnitConversionId { get; set; }

    public ProductUnitConversion? ProductUnitConversion { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,6)")]
    public decimal Factor { get; set; } = 1;

    [Column(TypeName = "decimal(18,3)")]
    public decimal BaseQuantity { get; set; }

    /// <summary>
    /// Bắt buộc khi AdjustmentIncrease.
    /// </summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal? UnitCost { get; set; }

    /// <summary>
    /// Dùng khi AdjustmentDecrease cần giá vốn tạm.
    /// </summary>
    [Column(TypeName = "decimal(18,6)")]
    public decimal? ProvisionalUnitCost { get; set; }

    [StringLength(1000)]
    public string? Note { get; set; }
}