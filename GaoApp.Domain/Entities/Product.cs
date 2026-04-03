using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Product")]
public class Product : BaseStoreEntity, IAuditTrackedEntity
{
    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(200)]
    public string Name { get; set; } = default!;

    [Required(ErrorMessage = "Alias không hợp lệ.")]
    [StringLength(200)]
    public string Alias { get; set; } = default!;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn danh mục.")]
    public int CategoryId { get; set; }
    public Category Category { get; set; } = default!;

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn nhà cung cấp.")]
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = default!;

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public int? TaxId { get; set; }
    public Tax? Tax { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn đơn vị.")]
    public int BaseUnitId { get; set; }
    public Unit BaseUnit { get; set; } = default!;

    [Range(0, double.MaxValue, ErrorMessage = "Giá cơ bản không hợp lệ.")]
    public decimal BasePrice { get; set; }

    [StringLength(500)]
    public string? Description { get; set; }

    public string? Content { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<ProductVariant> Variants { get; set; } = new List<ProductVariant>();
    public ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();


}
