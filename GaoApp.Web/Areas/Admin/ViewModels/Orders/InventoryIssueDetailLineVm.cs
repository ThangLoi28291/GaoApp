using GaoApp.Domain.Enums;

namespace GaoApp.Web.Areas.Admin.ViewModels.Orders;



public sealed class InventoryIssueDetailLineVm
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int ProductVariantId { get; set; }
    public int OrderLineId { get; set; }

    public string ProductName { get; set; } = string.Empty;
    public string? VariantSku { get; set; }

    public decimal OrderedQty { get; set; }
    public decimal StockBefore { get; set; }
    public decimal StockAfter { get; set; }
    public decimal NegativeQty { get; set; }

    public decimal CurrentAvailableQty { get; set; }

    public decimal? ProvisionalUnitCost { get; set; }
    public decimal? ProvisionalCostAmount { get; set; }
    public decimal? RevaluationAmount { get; set; }

    public bool HasLinkedReceipt { get; set; }
    public bool HasLinkedAdjustment { get; set; }

    public bool QuantityResolved { get; set; }
    public bool DocumentResolved { get; set; }
    public bool CostResolved { get; set; }

    public bool IsResolved { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }

    public List<string> ResolveReasons { get; set; } = new();

    public decimal AutoDetectedInboundQty { get; set; }
    public string? AutoResolveNote { get; set; }
}
