namespace GaoApp.Application.DTOs.POS;

public class POSProductSearchUnitOptionDto
{
    public int ProductUnitConversionId { get; set; }
    public int UnitId { get; set; }
    public string UnitName { get; set; } = "";
    public decimal Factor { get; set; }
    public decimal Price { get; set; }
    public string? Barcode { get; set; }
    public decimal AvailableQty { get; set; }
    public bool IsNegativeStock { get; set; }
}