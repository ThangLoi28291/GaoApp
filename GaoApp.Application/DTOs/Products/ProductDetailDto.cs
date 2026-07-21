namespace GaoApp.Application.DTOs.Products;

public sealed class ProductDetailDto
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string Alias { get; set; } = default!;

    public string? PrimaryImageUrl { get; set; }
    public List<ProductImageItemDto> Images { get; set; } = new();
}

public sealed class ProductImageItemDto
{
    public int Id { get; set; }
    public string Url { get; set; } = default!;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}
