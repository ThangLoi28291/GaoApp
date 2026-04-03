namespace GaoApp.Application.DTOs.Orders;

/// <summary>
/// Kết quả validate/resolve của 1 issue line.
/// Không lưu DB, chỉ dùng cho workflow mức 2 + UI.
/// </summary>
public sealed class InventoryIssueLineResolutionDto
{
    public int IssueLineId { get; set; }
    public int OrderLineId { get; set; }
    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantSku { get; set; }

    public decimal OrderedQty { get; set; }
    public decimal StockBefore { get; set; }
    public decimal StockAfter { get; set; }
    public decimal NegativeQty { get; set; }

    public decimal? ProvisionalUnitCost { get; set; }
    public decimal? ProvisionalCostAmount { get; set; }
    public decimal? RevaluationAmount { get; set; }

    public decimal CurrentAvailableQty { get; set; }

    public bool HasLinkedReceipt { get; set; }
    public bool HasLinkedAdjustment { get; set; }

    public bool QuantityResolved { get; set; }
    public bool DocumentResolved { get; set; }
    public bool CostResolved { get; set; }

    public bool IsResolved { get; set; }

    public List<string> Reasons { get; set; } = new();
}