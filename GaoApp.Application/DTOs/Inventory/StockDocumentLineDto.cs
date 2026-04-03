namespace GaoApp.Application.DTOs.Inventory;

public class StockDocumentLineDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }

    public int ProductVariantId { get; set; }

    public int? UnitId { get; set; }
    public string? UnitName { get; set; }

    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }

    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }

    public string ProductNameSnapshot { get; set; } = default!;
    public string? SkuSnapshot { get; set; }
    public string? BarcodeSnapshot { get; set; }

    public string? Note { get; set; }
    public List<StockDocumentUnitOptionDto> AvailableUnits { get; set; } = new();
}