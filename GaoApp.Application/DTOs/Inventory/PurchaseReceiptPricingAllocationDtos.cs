using GaoApp.Domain.Enums;
using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class PurchaseReceiptPricingApplyRequest
{
    public string ReceiptRowVersion { get; set; } = "";
    public string PlanRowVersion { get; set; } = "";
}

public sealed class PurchaseReceiptPricingConflictException(string message) : BusinessRuleException(message);

public sealed class PurchaseReceiptPricingAllocationRequest
{
    public string ReceiptRowVersion { get; set; } = "";
    public string? PlanRowVersion { get; set; }
    public decimal ActualBillTotal { get; set; }
    public decimal GlobalDiscountPercent { get; set; }
    public List<PurchaseReceiptPricingLineInput> Lines { get; set; } = [];
    public List<PurchaseReceiptPricingRuleInput> Rules { get; set; } = [];
    public List<PurchaseReceiptGiftValuationInput> GiftValuations { get; set; } = [];
    // Null keeps existing saved plans/API clients on the physical-line contract.
    public List<PurchaseReceiptBillLineInput>? BillLines { get; set; }
}

public sealed class PurchaseReceiptBillLineInput
{
    public string BillLineKey { get; set; } = "";
    public int LineNo { get; set; }
    public int ProductVariantId { get; set; }
    public int BillUnitId { get; set; }
    public decimal BillQuantity { get; set; }
    public decimal BillUnitPriceBeforeVat { get; set; }
    public bool IsGift { get; set; }
}

public sealed class PurchaseReceiptBillRuleSourceInput
{
    public string BillLineKey { get; set; } = "";
    public decimal Quantity { get; set; }
}

public sealed class PurchaseReceiptPricingLineInput
{
    public int StockDocumentLineId { get; set; }
    public string RowVersion { get; set; } = "";
    public int BillUnitId { get; set; }
    public decimal BillQuantity { get; set; }
    public decimal BillUnitPriceBeforeVat { get; set; }
}

public sealed class PurchaseReceiptPricingRuleInput
{
    public string Name { get; set; } = "";
    public string ProgramKey { get; set; } = "";
    public string RuleKey { get; set; } = "";
    public PurchaseReceiptPricingRuleType Type { get; set; }
    public List<int> SourceLineIds { get; set; } = [];
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public int? GiftLineId { get; set; }
    public int? GiftUnitId { get; set; }
    public decimal GiftQuantity { get; set; }
    public PurchaseReceiptPricingGiftMode GiftMode { get; set; }
    public List<PurchaseReceiptBillRuleSourceInput> BillSources { get; set; } = [];
    public string? GiftBillLineKey { get; set; }
}

public sealed class PurchaseReceiptGiftValuationInput
{
    public int ProductVariantId { get; set; }
    public int UnitId { get; set; }
    public decimal? ManualUnitValueBeforeVat { get; set; }
}

public sealed class PurchaseReceiptPricingPhysicalLine
{
    public int StockDocumentLineId { get; set; }
    public int LineNo { get; set; }
    public int ProductVariantId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public string? ProductImageUrl { get; set; }
    public int UnitId { get; set; }
    public int? ProductUnitConversionId { get; set; }
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }
    public string RowVersion { get; set; } = "";
    public string ProductRowVersion { get; set; } = "";
    public decimal CurrentUnitPriceBeforeVat { get; set; }
    public decimal CurrentAmountBeforeVat { get; set; }
    public List<PurchaseReceiptPricingUnit> Units { get; set; } = [];
}

public sealed record PurchaseReceiptPricingUnit(int UnitId, string Name, int ConversionId,
    decimal Factor, string RowVersion, string UnitRowVersion);

public sealed record PurchaseReceiptPricingGiftValue(int ProductVariantId, int UnitId,
    decimal Factor, decimal UnitValueBeforeVat, PurchaseReceiptGiftValuationSource Source,
    int? HistoricalReceiptLineId = null, DateTime? HistoricalConfirmedAtUtc = null);

public sealed class PurchaseReceiptPricingLineResult
{
    public int StockDocumentLineId { get; set; }
    public decimal BillFactor { get; set; }
    public decimal PurchasedBaseQuantity { get; set; }
    public decimal GiftBaseQuantity { get; set; }
    public decimal BaselineAmount { get; set; }
    public decimal GiftBurden { get; set; }
    public decimal PurchasedAmount { get; set; }
    public decimal GiftAmount { get; set; }
    public decimal FinalAmountBeforeVat { get; set; }
    public decimal EffectiveUnitPriceBeforeVat { get; set; }
}

public sealed record PurchaseReceiptPricingRuleResult(string RuleKey, decimal Amount,
    IReadOnlyDictionary<int, decimal> SourceAmounts);
public sealed record PurchaseReceiptPricingResidual(string Stage, string? RuleKey,
    int StockDocumentLineId, decimal Amount);

public sealed class PurchaseReceiptPricingAllocationPreview
{
    public bool CanApply => Errors.Count == 0;
    public List<string> Errors { get; set; } = [];
    public decimal ActualBillTotal { get; set; }
    public decimal SystemTotal { get; set; }
    public decimal Difference { get; set; }
    public List<PurchaseReceiptPricingLineResult> Lines { get; set; } = [];
    public List<PurchaseReceiptPricingRuleResult> Rules { get; set; } = [];
    public List<PurchaseReceiptPricingResidual> Residuals { get; set; } = [];
    public List<PurchaseReceiptPricingGiftValue> GiftValues { get; set; } = [];
    public List<PurchaseReceiptQuantityReconciliation> Quantities { get; set; } = [];
    public List<PurchaseReceiptBillSourceResult> BillSources { get; set; } = [];
}

public sealed record PurchaseReceiptQuantityReconciliation(int ProductVariantId, decimal ReceivedBaseQuantity,
    decimal PurchasedBaseQuantity, decimal GiftBaseQuantity);
public sealed record PurchaseReceiptBillSourceResult(string RuleKey, string BillLineKey, decimal BaselineAmount,
    decimal Amount, decimal ResidualAmount);

public sealed class PurchaseReceiptPricingAllocationWorkspace
{
    public int? PlanId { get; set; }
    public PurchaseReceiptPricingPlanState? State { get; set; }
    public string ReceiptRowVersion { get; set; } = "";
    public string? PlanRowVersion { get; set; }
    public bool CanEdit { get; set; }
    public bool IsStale { get; set; }
    public List<PurchaseReceiptPricingPhysicalLine> PhysicalLines { get; set; } = [];
    public PurchaseReceiptPricingAllocationRequest Draft { get; set; } = new();
    public PurchaseReceiptPricingAllocationPreview? Preview { get; set; }
}
