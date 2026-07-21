namespace GaoApp.Application.DTOs.Inventory;

/// <summary>
/// Dòng kiểm kê hiển thị trên detail phiếu.
/// </summary>
public class StockCountLineDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }

    public int ProductVariantId { get; set; }

    public int UnitId { get; set; }
    public string? UnitName { get; set; }
    public string? ImageUrl { get; set; }

    public decimal Factor { get; set; }

    public decimal SystemQtyBase { get; set; }
    public decimal CountedQty { get; set; }
    public decimal CountedQtyBase { get; set; }
    public decimal DifferenceQtyBase { get; set; }

    public string ProductNameSnapshot { get; set; } = string.Empty;
    public string? SkuSnapshot { get; set; }
    public string? BarcodeSnapshot { get; set; }

    public string? Note { get; set; }
}