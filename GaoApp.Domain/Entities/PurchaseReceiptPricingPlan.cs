using GaoApp.Domain.Common;
using GaoApp.Domain.Enums;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptPricingPlan : BaseStoreEntity
{
    public int StockDocumentId { get; set; }
    public StockDocument StockDocument { get; set; } = null!;
    public PurchaseReceiptPricingPlanState State { get; set; } = PurchaseReceiptPricingPlanState.Draft;
    public decimal ActualBillTotal { get; set; }
    public decimal GlobalDiscountPercent { get; set; }
    public decimal SystemTotal { get; set; }
    public int BillLayoutVersion { get; set; }
    public ICollection<PurchaseReceiptBillLine> BillLines { get; set; } = new List<PurchaseReceiptBillLine>();
    public byte[] ReceiptRowVersionSnapshot { get; set; } = [];
    public string PhysicalDependencyHash { get; set; } = "";
    public DateTime? AppliedAtUtc { get; set; }
    public DateTime? ConfirmedAtUtc { get; set; }
    public ICollection<PurchaseReceiptPricingPlanLine> Lines { get; set; } = new List<PurchaseReceiptPricingPlanLine>();
    public ICollection<PurchaseReceiptPricingRule> Rules { get; set; } = new List<PurchaseReceiptPricingRule>();
    public ICollection<PurchaseReceiptGiftValuation> GiftValuations { get; set; } = new List<PurchaseReceiptGiftValuation>();
}
