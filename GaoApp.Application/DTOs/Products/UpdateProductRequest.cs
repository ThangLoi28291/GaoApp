namespace GaoApp.Application.DTOs.Products;

public class UpdateProductRequest
{
    public int Id { get; set; }

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

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public List<string> TempImageTokens { get; set; } = new();
    public string? PrimaryTempToken { get; set; }
    public string? ImagesStateJson { get; set; }
    public string? PrimaryKey { get; set; }
}