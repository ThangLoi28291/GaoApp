using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptPricingRule : BaseStoreEntity
{
    public int PricingPlanId { get; set; }
    public PurchaseReceiptPricingPlan PricingPlan { get; set; } = null!;
    public string RuleKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string ProgramKey { get; set; } = "";
    public int? GiftBillLineId { get; set; }
    public PurchaseReceiptBillLine? GiftBillLine { get; set; }
    public PurchaseReceiptPricingRuleType Type { get; set; }
    public decimal DiscountPercent { get; set; }
    public PurchaseReceiptPricingGiftMode? GiftMode { get; set; }
    public int? GiftPlanLineId { get; set; }
    public PurchaseReceiptPricingPlanLine? GiftPlanLine { get; set; }
    public int? GiftUnitId { get; set; }
    public int? GiftConversionId { get; set; }
    public decimal GiftFactor { get; set; }
    public decimal GiftQuantity { get; set; }
    public decimal Amount { get; set; }
    public ICollection<PurchaseReceiptPricingRuleSource> Sources { get; set; } = new List<PurchaseReceiptPricingRuleSource>();
}
