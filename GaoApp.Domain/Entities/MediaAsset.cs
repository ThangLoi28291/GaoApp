using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("MediaAssets")]
public class MediaAsset : BaseStoreEntity
{
    [Required, StringLength(260)]
    public string StoragePath { get; set; } = default!;
    // vd: uploads/_temp/2025/12/22/{token}/abc.png
    // hoặc: uploads/products/2025/12/22/abc.png

    [StringLength(260)]
    public string? OriginalFileName { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    [StringLength(64)]
    public string? Sha256 { get; set; }

    /* =======================
       🔥 BỔ SUNG CHO TEMP FLOW
       ======================= */

    public bool IsTemp { get; set; } = false;

    [StringLength(80)]
    public string? TempToken { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpireAtUtc { get; set; }  // ✅ hết hạn temp (UTC)


    // Navigation
    public ICollection<ProductImage> ProductImages { get; set; } = new List<ProductImage>();
}

