namespace GaoApp.Application.DTOs.Returns;


public sealed class OrderReturnEligibilityLineDto
{
    public int OrderLineId { get; set; }
    public int VariantId { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public string? UnitName { get; set; }
    public decimal SoldQuantity { get; set; }
    public decimal ReturnedQuantity { get; set; }
    public decimal ReturnableQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineDiscount { get; set; }
    public decimal SuggestedRefundUnitAmount { get; set; }
    public decimal Multiplier { get; set; }
    public bool CanRestock { get; set; } = true;
    public string? RestockBlockCode { get; set; }
    public string? RestockBlockReason { get; set; }
    public string? RestockActionHint { get; set; }
}
