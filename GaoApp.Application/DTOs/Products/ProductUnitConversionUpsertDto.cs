using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

public class ProductUnitConversionUpsertDto
{
    public int? Id { get; set; }

    [Range(1, int.MaxValue)]
    public int ProductVariantId { get; set; }

    [Range(1, int.MaxValue)]
    public int UnitId { get; set; }

    [Range(typeof(decimal), "0.0001", "999999999")]
    public decimal Factor { get; set; } = 1;

    public bool IsBaseUnit { get; set; } = false;
    public bool IsDefaultForSale { get; set; } = false;
    public decimal? Price { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; } = 0;
}