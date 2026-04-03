namespace GaoApp.Application.DTOs.Products;

public class UpsertProductUnitConversionRequest
{
    public int Id { get; set; }
    public int ProductVariantId { get; set; }
    public int UnitId { get; set; }
    public string UnitName { get; set; } = default!;
    public decimal Factor { get; set; }
    public bool IsBaseUnit { get; set; }
    public bool IsDefaultForSale { get; set; }
    public decimal? Price { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }

    public List<ProductVariantUnitBarcodeDto> Barcodes { get; set; } = new();
}