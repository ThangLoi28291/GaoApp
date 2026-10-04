using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptGiftValuation : BaseStoreEntity
{
    public int PricingPlanId { get; set; }
    public PurchaseReceiptPricingPlan PricingPlan { get; set; } = null!;
    public int ProductVariantId { get; set; }
    public int UnitId { get; set; }
    public int ConversionId { get; set; }
    public decimal Factor { get; set; }
    public decimal UnitValueBeforeVat { get; set; }
    public PurchaseReceiptGiftValuationSource Source { get; set; }
    public int? HistoricalReceiptLineId { get; set; }
    public DateTime? HistoricalConfirmedAtUtc { get; set; }
}
