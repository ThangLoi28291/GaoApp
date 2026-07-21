using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("PurchaseRequestLines")]
public sealed class PurchaseRequestLine : BaseStoreEntity, IAuditTrackedEntity
{
    public int PurchaseRequestId { get; set; }
    public PurchaseRequest PurchaseRequest { get; set; } = null!;
    public int LineNo { get; set; }

    public PurchaseItemKind ItemKind { get; set; } = PurchaseItemKind.Catalog;

    public int? ProductVariantId { get; set; }
    public ProductVariant? ProductVariant { get; set; }
    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public ProductUnitConversion? ProductUnitConversion { get; set; }

    [Required, StringLength(250)]
    public string ProductNameSnapshot { get; set; } = string.Empty;

    [StringLength(100)]
    public string? SkuSnapshot { get; set; }

    [Required, StringLength(100)]
    public string UnitNameSnapshot { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,4)")]
    public decimal ConversionFactor { get; set; }

    /// <summary>Số lượng nguyên gốc do nhân viên đề nghị; không bị ghi đè khi duyệt.</summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal RequestedQuantity { get; set; }

    /// <summary>Số lượng quản lý chấp thuận đặt. Null khi chưa duyệt.</summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal? ApprovedQuantity { get; set; }

    /// <summary>Tổng số lượng đã liên kết sang các dòng PurchaseOrder.</summary>
    [Column(TypeName = "decimal(18,3)")]
    public decimal ConvertedQuantity { get; set; }

    public ICollection<PurchaseOrderLine> PurchaseOrderLines { get; set; } = new List<PurchaseOrderLine>();
}
