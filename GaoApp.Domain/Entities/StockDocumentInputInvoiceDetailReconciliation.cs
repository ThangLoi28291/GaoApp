using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("StockDocumentInputInvoiceDetailReconciliation")]
public sealed class StockDocumentInputInvoiceDetailReconciliation : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentInputInvoiceReconciliationId { get; set; }
    public int StockDocumentId { get; set; }
    public int InputInvoiceHeadId { get; set; }
    public int InputInvoiceDetailId { get; set; }
    public InputInvoiceDetailReconciliationState DetailState { get; set; }

    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? ConfirmedUnitId { get; set; }
    public int? ConfirmedBaseUnitId { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal? ConfirmedFactor { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal XmlQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal DerivedBaseQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal ReceiptBaseQuantity { get; set; }
    [Column(TypeName = "decimal(18,4)")]
    public decimal QuantityDifference { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceiptBeforeVatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal XmlBeforeVatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal AmountDifference { get; set; }
    [Column(TypeName = "decimal(9,4)")]
    public decimal? ReceiptVatRate { get; set; }
    [Column(TypeName = "decimal(9,4)")]
    public decimal? XmlVatRate { get; set; }
    public bool VatComparable { get; set; }
    [StringLength(500)]
    public string? VatComparisonReason { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceiptVatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal XmlVatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal VatAmountDifference { get; set; }

    public bool IsIgnored { get; set; }
    [StringLength(1000)]
    public string? IgnoreReason { get; set; }

    public StockDocumentInputInvoiceReconciliation Reconciliation { get; set; } = default!;
    public InputInvoiceDetail InputInvoiceDetail { get; set; } = default!;
}
