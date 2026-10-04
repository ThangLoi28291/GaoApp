using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

public sealed class PurchaseReceiptPricingPlanLine : BaseStoreEntity
{
    public int PricingPlanId { get; set; }
    public PurchaseReceiptPricingPlan PricingPlan { get; set; } = null!;
    public int StockDocumentLineId { get; set; }
    public StockDocumentLine StockDocumentLine { get; set; } = null!;
    public int LineNo { get; set; }
    public int ProductVariantIdSnapshot { get; set; }
    public int ProductIdSnapshot { get; set; }
    public int PhysicalUnitIdSnapshot { get; set; }
    public int? PhysicalConversionIdSnapshot { get; set; }
    public decimal PhysicalFactorSnapshot { get; set; }
    public decimal PhysicalQuantitySnapshot { get; set; }
    public decimal PhysicalBaseQuantitySnapshot { get; set; }
    public byte[] ReceiptLineRowVersionSnapshot { get; set; } = [];
    public int BillUnitId { get; set; }
    public int BillConversionId { get; set; }
    public decimal BillFactor { get; set; }
    public decimal BillQuantity { get; set; }
    public decimal BillUnitPriceBeforeVat { get; set; }
    public decimal PurchasedBaseQuantity { get; set; }
    public decimal GiftBaseQuantity { get; set; }
    public decimal BaselineAmount { get; set; }
    public decimal BaselineResidual { get; set; }
    public decimal GiftBurden { get; set; }
    public decimal PurchasedAmount { get; set; }
    public decimal GiftAmount { get; set; }
    public decimal FinalAmountBeforeVat { get; set; }
    public decimal EffectiveUnitPriceBeforeVat { get; set; }
}
