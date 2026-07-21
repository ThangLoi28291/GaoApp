namespace GaoApp.Application.DTOs.Products;

public class ProductListItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Alias { get; set; } = default!;

    public string? CategoryName { get; set; }
    public string? SupplierName { get; set; }
    public string? BrandName { get; set; }
    public string? TaxName { get; set; }
    public string? BaseUnitName { get; set; }

    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; }
    public bool IsSellable { get; set; }
    public bool HasVariants { get; set; }
    public string? PrimaryImageUrl { get; set; }
    public int ImageCount { get; set; }

}
