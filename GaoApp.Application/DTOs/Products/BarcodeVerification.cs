namespace GaoApp.Application.DTOs.Products.BarcodeVerification;

public class MissingBarcodeUnitDto
{
    public int ProductVariantId { get; set; }
    public int ProductUnitConversionId { get; set; }

    public string ProductName { get; set; } = default!;
    public string UnitName { get; set; } = default!;
    public decimal Factor { get; set; }

    public string? LegacyBarcode { get; set; }
    public string? ImageUrl { get; set; }
}