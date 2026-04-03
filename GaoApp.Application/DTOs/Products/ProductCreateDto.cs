namespace GaoApp.Application.DTOs.Products;

public class ProductCreateDto
{
    public string? Name { get; set; }

    public string? Alias { get; set; }

    public int? CategoryId { get; set; }

    public int? SupplierId { get; set; }

    public int? BrandId { get; set; }
    public int? TaxId { get; set; }

    public int? BaseUnitId { get; set; }

    public decimal BasePrice { get; set; }

    public string? Description { get; set; }

    public string? Content { get; set; }

    public List<string> TempImageTokens { get; set; } = new();
    public string? PrimaryTempToken { get; set; }
}