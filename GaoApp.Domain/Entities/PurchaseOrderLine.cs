using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseOrderLines")]
public sealed class PurchaseOrderLine : BaseStoreEntity, IAuditTrackedEntity
{
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public int? SourcePurchaseRequestLineId { get; set; }
    public PurchaseRequestLine? SourcePurchaseRequestLine { get; set; }
    public int LineNo { get; set; }

    public PurchaseItemKind ItemKind { get; set; } = PurchaseItemKind.Catalog;

    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public ProductUnitConversion? ProductUnitConversion { get; set; }
    public int? TaxId { get; set; }
    public Tax? Tax { get; set; }

    [Required, StringLength(250)]
    public string ProductNameSnapshot { get; set; } = string.Empty;
    [StringLength(100)]
    public string? SkuSnapshot { get; set; }
    [Required, StringLength(100)]
    public string UnitNameSnapshot { get; set; } = string.Empty;
    [StringLength(100)]
    public string? TaxNameSnapshot { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ConversionFactor { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal OrderedQuantity { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPriceBeforeVat { get; set; }
    [Column(TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal VatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPriceAfterVat { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotalBeforeVat { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal LineTotalAfterVat { get; set; }

    [Column(TypeName = "decimal(18,3)")]
    public decimal ReceivedQuantity { get; set; }
    [Column(TypeName = "decimal(18,3)")]
    public decimal ShortClosedQuantity { get; set; }
    public PurchaseOrderLineReceiptStatus ReceiptStatus { get; set; } = PurchaseOrderLineReceiptStatus.NotReceived;
    [StringLength(500)]
    public string? ShortCloseReason { get; set; }
    public DateTime? ShortClosedAtUtc { get; set; }
    public int? ShortClosedByUserId { get; set; }

    /// <summary>
    /// Dấu vết liên kết một dòng mô tả với sản phẩm danh mục trước khi ghi nhận nhập kho.
    /// ItemKind vẫn là FreeText để không làm mất nguồn gốc mô tả ban đầu.
    /// </summary>
    public DateTime? ResolvedAtUtc { get; set; }
    public int? ResolvedByUserId { get; set; }
    [StringLength(500)]
    public string? ResolutionNote { get; set; }

    [NotMapped]
    public decimal PendingQuantity => Math.Max(0m, OrderedQuantity - ReceivedQuantity - ShortClosedQuantity);
}
