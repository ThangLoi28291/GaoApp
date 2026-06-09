namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Giá vốn đề xuất khi duyệt phiếu tăng kho.
/// </summary>
public class InventoryCostSuggestionDto
{
    public int ProductVariantId { get; set; }

    public decimal? UnitCost { get; set; }

    public string SourceType { get; set; } = string.Empty;

    public string SourceText { get; set; } = string.Empty;

    public bool HasValidCost => UnitCost.HasValue && UnitCost.Value > 0;
}