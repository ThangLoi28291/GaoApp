using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

public sealed class ProductEditDto
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(200)]
    public string Name { get; set; } = default!;

    [Required(ErrorMessage = "Alias không hợp lệ.")]
    [StringLength(200)]
    public string Alias { get; set; } = default!;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    public int CategoryId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn nhà cung cấp.")]
    public int SupplierId { get; set; }

    public int? BrandId { get; set; }
    public int? TaxId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đơn vị.")]
    public int BaseUnitId { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "Giá cơ bản không hợp lệ.")]
    public decimal BasePrice { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public string? Content { get; set; }

    public bool IsSellable { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // ✅ dùng cho preload FilePond
    public List<ProductImageEditItemDto> Images { get; set; } = new();
}

