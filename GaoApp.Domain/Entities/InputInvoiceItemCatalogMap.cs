using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("InputInvoiceItemCatalogMap")]
public sealed class InputInvoiceItemCatalogMap : BaseStoreEntity
{
    public int SupplierId { get; set; }

    [StringLength(200)]
    public string? SupplierItemCode { get; set; }

    [StringLength(200)]
    public string? NormalizedSupplierItemCode { get; set; }

    [Required, StringLength(500)]
    public string SupplierItemName { get; set; } = string.Empty;

    [Required, StringLength(500)]
    public string NormalizedSupplierItemName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string SupplierUnitName { get; set; } = string.Empty;

    [Required, StringLength(100)]
    public string NormalizedSupplierUnitName { get; set; } = string.Empty;

    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }
    public int ConfirmedUnitId { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal ConfirmedFactor { get; set; }

    public int ConfirmedBaseUnitId { get; set; }
    public bool IsActive { get; set; } = true;

    public Supplier Supplier { get; set; } = default!;
    public ProductVariant ProductVariant { get; set; } = default!;
    public ProductUnitConversion ProductUnitConversion { get; set; } = default!;
    public Unit ConfirmedUnit { get; set; } = default!;
    public Unit ConfirmedBaseUnit { get; set; } = default!;
}
