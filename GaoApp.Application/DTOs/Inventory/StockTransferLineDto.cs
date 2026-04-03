namespace GaoApp.Application.DTOs.Inventory;

public class StockTransferLineDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }

    public int ProductVariantId { get; set; }
    public int UnitId { get; set; }

    public string UnitNameSnapshot { get; set; } = default!;
    public decimal Factor { get; set; }
    public decimal Quantity { get; set; }
    public decimal BaseQuantity { get; set; }

    public string ProductNameSnapshot { get; set; } = default!;
    public string? SkuSnapshot { get; set; }
    public string? BarcodeSnapshot { get; set; }

    public string? Note { get; set; }
}