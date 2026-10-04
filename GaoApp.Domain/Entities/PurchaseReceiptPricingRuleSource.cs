using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptPricingRuleSource : BaseStoreEntity
{
    public int PricingPlanId { get; set; }
    public int PricingRuleId { get; set; }
    public PurchaseReceiptPricingRule PricingRule { get; set; } = null!;
    public int PricingPlanLineId { get; set; }
    public PurchaseReceiptPricingPlanLine PricingPlanLine { get; set; } = null!;
    public decimal BaselineAmountSnapshot { get; set; }
    public decimal Amount { get; set; }
    public decimal ResidualAmount { get; set; }
    public int? BillLineId { get; set; }
    public PurchaseReceiptBillLine? BillLine { get; set; }
    public decimal? ParticipatingQuantity { get; set; }
}
