using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

public sealed class ProductImageEditItemDto
{
    public int ProductImageId { get; set; }
    public string Url { get; set; } = default!;
    public bool IsPrimary { get; set; }
    public int SortOrder { get; set; }
}
