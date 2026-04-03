namespace GaoApp.Application.DTOs.Products;

public sealed class ProductImageStateDto
{
    public int? ExistingProductImageId { get; set; } // ảnh cũ
    public string? TempToken { get; set; }           // ảnh mới
}
