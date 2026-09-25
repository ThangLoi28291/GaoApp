using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

[Table("StockDocumentInputInvoiceReconciliation")]
public sealed class StockDocumentInputInvoiceReconciliation : BaseStoreEntity, IAuditTrackedEntity
{
    public int StockDocumentInputInvoiceMapId { get; set; }
    public int StockDocumentId { get; set; }
    public int InputInvoiceHeadId { get; set; }
    public InputInvoiceReconciliationState OverallState { get; set; }

    [Required, StringLength(64)]
    public string EvidenceFingerprint { get; set; } = string.Empty;
    public DateTime LastCalculatedAtUtc { get; set; }

    public int TotalDetailCount { get; set; }
    public int MatchedDetailCount { get; set; }
    public int MismatchDetailCount { get; set; }
    public int UnmatchedDetailCount { get; set; }
    public int IgnoredDetailCount { get; set; }
    public int ExcludedLineCount { get; set; }
    public int IncompleteCount { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceiptSubtotalBeforeVat { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal XmlTotalBeforeTax { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal SubtotalDifference { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceiptVatAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal XmlTaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal VatDifference { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal ReceiptGoodsTotal { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal XmlPaymentAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")]
    public decimal PaymentDifference { get; set; }
    public bool HasUnsupportedHeader { get; set; }
    [StringLength(500)]
    public string? UnsupportedHeaderReason { get; set; }

    [StringLength(64)]
    public string? AcceptedEvidenceFingerprint { get; set; }
    [StringLength(1000)]
    public string? AcceptanceReason { get; set; }
    public int? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }

    public StockDocumentInputInvoiceMap StockDocumentInputInvoiceMap { get; set; } = default!;
    public StockDocument StockDocument { get; set; } = default!;
    public InputInvoiceHead InputInvoiceHead { get; set; } = default!;
    public ICollection<StockDocumentInputInvoiceDetailReconciliation> Details { get; set; } = [];
}
