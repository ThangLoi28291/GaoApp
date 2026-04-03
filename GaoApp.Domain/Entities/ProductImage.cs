using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("ProductImages")]
public class ProductImage : BaseStoreEntity
{
    // FK Product (bạn đã có Product entity)
    public int ProductId { get; set; }

    // FK MediaAsset
    public int MediaAssetId { get; set; }

    public bool IsPrimary { get; set; } = false;
    public int SortOrder { get; set; } = 0;

    // Optional caption/alt
    [StringLength(200)]
    public string? AltText { get; set; }

    // Navigation
    public MediaAsset MediaAsset { get; set; } = default!;

    // Nếu bạn có Product entity thì bật lại:
    public Product Product { get; set; } = default!;
}
