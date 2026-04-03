namespace GaoApp.Application.DTOs.Products;

public class SaveProductVariantsRequest
{
    public int ProductId { get; set; }
    public List<ProductVariantRowDto> Variants { get; set; } = new();
}
