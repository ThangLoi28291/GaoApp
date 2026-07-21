namespace GaoApp.Application.DTOs.Orders.LegalEntityAllocation;

/// <summary>
/// Kết quả nhánh finalize đa LegalEntity. POS dùng kết quả này để giữ nguyên
/// workflow issue/cost hiện hữu mà không cần biết chi tiết persistence allocation.
/// </summary>
public sealed class OrderLegalEntityFinalizeResult
{
    public bool IsFeatureEnabled { get; init; }
    public bool WasApplied { get; init; }
    public int LegalEntityCount { get; init; }
    public bool HasMultipleLegalEntities => LegalEntityCount > 1;
    public int AllocationCount { get; init; }
    public bool HasNegativeInventory { get; init; }
    public bool HasProvisionalCost { get; init; }
    public IReadOnlyList<OrderLegalEntityFinalizeInventoryLine> IssueLines { get; init; }
        = Array.Empty<OrderLegalEntityFinalizeInventoryLine>();

    public static OrderLegalEntityFinalizeResult FeatureDisabled { get; } = new();
}

public sealed class OrderLegalEntityFinalizeInventoryLine
{
    public int OrderLineId { get; init; }
    public int ProductId { get; init; }
    public int VariantId { get; init; }
    public int? ProductUnitConversionId { get; init; }
    public string ItemName { get; init; } = string.Empty;
    public decimal BaseQuantity { get; init; }
    public bool IsNegativeInventory { get; init; }
    public decimal BeforeQty { get; init; }
    public decimal AfterQty { get; init; }
    public bool IsProvisionalCost { get; init; }
    public decimal? UnitCostSnapshot { get; init; }
    public decimal? ProvisionalUnitCost { get; init; }
    public string? Note { get; init; }
}
