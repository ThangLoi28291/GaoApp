namespace GaoApp.Application.DTOs.Promotions;

public sealed class PromotionProductUnitLookupDto
{
    public int? ProductUnitConversionId { get; set; }

    public int UnitId { get; set; }

    public string UnitName { get; set; } = "";

    public decimal Factor { get; set; }

    public decimal? RetailPrice { get; set; }

    public decimal? WholesalePrice { get; set; }

    public bool IsBaseUnit { get; set; }

    public string Text { get; set; } = "";
}