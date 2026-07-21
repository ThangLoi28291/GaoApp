namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// DTO hiển thị dòng phiếu điều chỉnh kho.
/// </summary>
public class InventoryAdjustmentLineDto
{
    public int Id { get; set; }

    public int ProductVariantId { get; set; }

    public string ProductName { get; set; } = string.Empty;

    public string ProductVariantName { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int? UnitId { get; set; }

    public string UnitName { get; set; } = string.Empty;

    public int? ProductUnitConversionId { get; set; }

    public decimal Quantity { get; set; }

    public decimal Factor { get; set; }

    public decimal BaseQuantity { get; set; }

    public decimal? UnitCost { get; set; }

    public decimal? ProvisionalUnitCost { get; set; }

    public string? Note { get; set; }
    public string? ImageUrl { get; set; }

    public decimal? Price { get; set; }

    public decimal? CostPrice { get; set; }
}