namespace GaoApp.Application.DTOs.Products;

public sealed class ProductImageListItemDto
{
    public int Id { get; set; }

    // URL public để hiển thị (đã có sẵn trong MediaAsset)
    public string Url { get; set; } = default!;

    // để UI biết đâu là ảnh chính
    public bool IsPrimary { get; set; }
}
