using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptBillLine : BaseStoreEntity
{
    public int PricingPlanId { get; set; }
    public PurchaseReceiptPricingPlan PricingPlan { get; set; } = null!;
    public string BillLineKey { get; set; } = "";
    public int LineNo { get; set; }
    public int ProductVariantIdSnapshot { get; set; }
    public int BillUnitId { get; set; }
    public string BillUnitName { get; set; } = "";
    public int BillConversionId { get; set; }
    public decimal BillFactor { get; set; }
    public decimal BillQuantity { get; set; }
    public decimal BillUnitPriceBeforeVat { get; set; }
    public bool IsGift { get; set; }
}
