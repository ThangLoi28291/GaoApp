using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class InputInvoiceReconciliationDto
{
    public int StockDocumentId { get; set; }
    public int? InputInvoiceHeadId { get; set; }
    public InputInvoiceReconciliationState State { get; set; }
    public string StateName => State.ToString();
    public bool ConfirmReady => State is InputInvoiceReconciliationState.NotApplicable
        or InputInvoiceReconciliationState.Incomplete
        or InputInvoiceReconciliationState.Matched
        or InputInvoiceReconciliationState.Mismatch
        or InputInvoiceReconciliationState.AcceptedMismatch;
    public string EvidenceFingerprint { get; set; } = string.Empty;
    public DateTime? LastCalculatedAtUtc { get; set; }
    public decimal QuantityTolerance { get; set; }
    public decimal MoneyTolerance { get; set; }
    public int ExcludedLineCount { get; set; }
    public string? AcceptanceReason { get; set; }
    public int? AcceptedByUserId { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public string? Message { get; set; }
    public bool IsConfirmedReadOnly { get; set; }
    public bool IsLateAssociationException { get; set; }
    public bool IsCommercialPreview { get; set; }
    public int ProductCount { get; set; }
    public int MatchedProductCount { get; set; }
    public int DifferingProductCount { get; set; }
    public int UnresolvedXmlDetailCount { get; set; }
    public InputInvoiceHeaderReconciliationDto? Header { get; set; }
    public List<InputInvoiceProductReconciliationSummaryDto> ProductSummaries { get; set; } = [];
    public List<InputInvoiceDetailReconciliationDto> Details { get; set; } = [];
    public List<InputInvoiceExcludedLineDto> ExcludedLines { get; set; } = [];
}

public sealed class InputInvoiceCommercialPreviewRequest
{
    public int StockDocumentId { get; set; }
    public string? RowVersion { get; set; }
    public bool HasVat { get; set; }
    public List<InputInvoiceCommercialPreviewLineRequest> Lines { get; set; } = [];
}

public sealed class InputInvoiceCommercialPreviewLineRequest
{
    public int StockDocumentLineId { get; set; }
    public decimal UnitPriceBeforeVat { get; set; }
    public int? TaxId { get; set; }
}

public sealed class InputInvoiceHeaderReconciliationDto
{
    public decimal ReceiptSubtotalBeforeVat { get; set; }
    public decimal XmlTotalBeforeTax { get; set; }
    public decimal SubtotalDifference { get; set; }
    public decimal ReceiptVatAmount { get; set; }
    public decimal XmlTaxAmount { get; set; }
    public decimal VatDifference { get; set; }
    public decimal ReceiptGoodsTotal { get; set; }
    public decimal XmlPaymentAmount { get; set; }
    public decimal PaymentDifference { get; set; }
    public bool NeedsReview { get; set; }
    public string? Reason { get; set; }
}

public sealed class InputInvoiceDetailReconciliationDto
{
    public int InputInvoiceDetailId { get; set; }
    public string? ItemName { get; set; }
    public string? UnitName { get; set; }
    public InputInvoiceDetailReconciliationState State { get; set; }
    public string StateName => State.ToString();
    public decimal XmlQuantity { get; set; }
    public decimal DerivedBaseQuantity { get; set; }
    public decimal ReceiptBaseQuantity { get; set; }
    public decimal QuantityDifference { get; set; }
    public int? MappingId { get; set; }
    public int? ProductVariantId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public int? ConfirmedUnitId { get; set; }
    public decimal? ConfirmedFactor { get; set; }
    public int? ConfirmedBaseUnitId { get; set; }
    public decimal ReceiptBeforeVatAmount { get; set; }
    public decimal XmlBeforeVatAmount { get; set; }
    public decimal AmountDifference { get; set; }
    public decimal? ReceiptBaseUnitPriceBeforeVat { get; set; }
    public decimal? XmlBaseUnitPriceBeforeVat { get; set; }
    public decimal? BaseUnitPriceDifference { get; set; }
    public decimal? ReceiptVatRate { get; set; }
    public decimal? XmlVatRate { get; set; }
    public decimal ReceiptVatAmount { get; set; }
    public decimal XmlVatAmount { get; set; }
    public decimal VatAmountDifference { get; set; }
    public bool IsIgnored { get; set; }
    public string? IgnoreReason { get; set; }
    public List<int> StockDocumentLineIds { get; set; } = [];
    public List<InputInvoiceReceiptLineContextDto> ReceiptLines { get; set; } = [];
    public string? Message { get; set; }
}

public sealed class InputInvoiceReceiptLineContextDto
{
    public int StockDocumentLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal Factor { get; set; }
    public decimal BaseQuantity { get; set; }
    public ReceiptAllocationKind ReceiptAllocationKind { get; set; }
    public OutsidePoDecisionStatus OutsidePoDecisionStatus { get; set; }
}

public sealed class InputInvoiceProductReconciliationSummaryDto
{
    public int ProductVariantId { get; set; }
    public string ProductDisplayName { get; set; } = string.Empty;
    public List<InputInvoiceReceiptLineContextDto> ReceiptLines { get; set; } = [];
    public List<InputInvoiceProductXmlDetailContextDto> XmlDetails { get; set; } = [];
    public int ReceiptLineCount { get; set; }
    public int XmlDetailCount { get; set; }
    public decimal ReceiptBaseQuantity { get; set; }
    public decimal XmlBaseQuantity { get; set; }
    public string BaseUnitDisplayName { get; set; } = "đơn vị gốc";
    public decimal QuantityDifference { get; set; }
    public string QuantityStatus { get; set; } = "InsufficientData";
    public decimal ReceiptBeforeVatAmount { get; set; }
    public decimal XmlBeforeVatAmount { get; set; }
    public decimal BeforeVatAmountDifference { get; set; }
    public decimal? ReceiptBaseUnitPriceBeforeVat { get; set; }
    public decimal? XmlBaseUnitPriceBeforeVat { get; set; }
    public decimal? BaseUnitPriceDifference { get; set; }
    public string PriceStatus { get; set; } = "InsufficientData";
    public decimal ReceiptVatAmount { get; set; }
    public decimal XmlVatAmount { get; set; }
    public decimal VatAmountDifference { get; set; }
    public string VatStatus { get; set; } = "NeedsReview";
}

public sealed class InputInvoiceProductXmlDetailContextDto
{
    public int InputInvoiceDetailId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal Quantity { get; set; }
    public decimal Factor { get; set; }
    public decimal BaseQuantity { get; set; }
}

public sealed class InputInvoiceExcludedLineDto
{
    public int StockDocumentLineId { get; set; }
    public string? Reason { get; set; }
}

public sealed class InputInvoiceReconciliationReasonRequest
{
    public string? Reason { get; set; }
}

public sealed class AcceptInputInvoiceReconciliationRequest
{
    public string? Reason { get; set; }
    public string? ExpectedEvidenceFingerprint { get; set; }
}
