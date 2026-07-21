using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Lịch sử thay đổi barcode theo đơn vị quy đổi.
/// Đây là bảng append-only để truy vết nghiệp vụ barcode.
/// </summary>
[Table("ProductVariantBarcodeHistory")]
public class ProductVariantBarcodeHistory : BaseStoreEntity, IAuditTrackedEntity
{
    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductUnitConversionId { get; set; }

    public int? OldBarcodeId { get; set; }

    public int? NewBarcodeId { get; set; }

    [StringLength(64)]
    public string? OldBarcode { get; set; }

    [StringLength(64)]
    public string? NewBarcode { get; set; }

    public BarcodeHistoryActionType ActionType { get; set; }

    [StringLength(500)]
    public string? Reason { get; set; }

    public int? ChangedByUserId { get; set; }

    [StringLength(200)]
    public string? ChangedByUserName { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    public ProductVariant ProductVariant { get; set; } = default!;
    public ProductUnitConversion ProductUnitConversion { get; set; } = default!;

    [ForeignKey(nameof(OldBarcodeId))]
    public ProductVariantUnitBarcode? OldBarcodeRecord { get; set; }

    [ForeignKey(nameof(NewBarcodeId))]
    public ProductVariantUnitBarcode? NewBarcodeRecord { get; set; }
}