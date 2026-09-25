using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Immutable-catalog physical evidence captured before a product is known.
/// It never participates directly in posting; only the resolved StockDocumentLine does.
/// </summary>
public sealed class StockDocumentProvisionalItem : BaseStoreEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = null!;

    [MaxLength(200)]
    public string NameSnapshot { get; set; } = string.Empty;

    [MaxLength(256)]
    public string? RawBarcodeSnapshot { get; set; }

    [MaxLength(64)]
    public string? NormalizedBarcode { get; set; }

    public int? UnitId { get; set; }
    public Unit? Unit { get; set; }

    [MaxLength(100)]
    public string? UnitNameSnapshot { get; set; }

    [MaxLength(100)]
    public string? NormalizedUnitNameSnapshot { get; set; }

    public decimal Quantity { get; set; }

    // The employee's declared packing specification; null for legacy captures.
    public int? ProposedProductVariantId { get; set; }
    public ProductVariant? ProposedProductVariant { get; set; }
    public int? ProposedBaseUnitId { get; set; }
    public Unit? ProposedBaseUnit { get; set; }
    [MaxLength(100)] public string? ProposedBaseUnitName { get; set; }
    public decimal? ProposedFactor { get; set; }
    public int? ProposedCategoryId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
    public byte[]? PackagingPhoto { get; set; }

    public StockDocumentProvisionalItemStatus Status { get; set; }
        = StockDocumentProvisionalItemStatus.Unresolved;
    public ProvisionalItemResolutionMethod? ResolutionMethod { get; set; }
    public int? ResolvedStockDocumentLineId { get; set; }
    public StockDocumentLine? ResolvedStockDocumentLine { get; set; }
    public int? ResolvedProductVariantId { get; set; }
    public ProductVariant? ResolvedProductVariant { get; set; }
    public int? ResolvedProductUnitConversionId { get; set; }
    public ProductUnitConversion? ResolvedProductUnitConversion { get; set; }
    public int? ResolvedByUserId { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public bool RawBarcodeRemembered { get; set; }
    public int? CreatedBarcodeId { get; set; }
    public ProductVariantUnitBarcode? CreatedBarcode { get; set; }
    public int? RemovedByUserId { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public int? SupersededByProvisionalItemId { get; set; }
    public StockDocumentProvisionalItem? SupersededByProvisionalItem { get; set; }
    public int ReceivingRevision { get; set; }
}
