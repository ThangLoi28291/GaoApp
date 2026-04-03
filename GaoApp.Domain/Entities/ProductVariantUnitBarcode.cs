using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

/// <summary>
/// Barcode thuộc về một đơn vị bán cụ thể của ProductVariant.
/// Đây là nguồn dữ liệu barcode duy nhất của hệ thống.
/// </summary>
[Table("ProductVariantUnitBarcode")]
public class ProductVariantUnitBarcode : BaseStoreEntity, IAuditTrackedEntity
{
    [Range(1, int.MaxValue)]
    public int ProductUnitConversionId { get; set; }

    [Required]
    [StringLength(64)]
    public string Barcode { get; set; } = default!;

    public BarcodeType BarcodeType { get; set; } = BarcodeType.External;

    public bool IsPrimary { get; set; } = true;

    public bool IsActive { get; set; } = true;

    [StringLength(250)]
    public string? Note { get; set; }

    public ProductUnitConversion ProductUnitConversion { get; set; } = default!;

    public ICollection<ProductVariantBarcodeHistory> HistoryAsOldBarcode { get; set; }
        = new List<ProductVariantBarcodeHistory>();

    public ICollection<ProductVariantBarcodeHistory> HistoryAsNewBarcode { get; set; }
        = new List<ProductVariantBarcodeHistory>();
}