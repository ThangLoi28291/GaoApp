using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("MediaAssets")]
public class MediaAsset : BaseStoreEntity
{
    [Required, StringLength(260)]
    public string StoragePath { get; set; } = default!;

    // Ví dụ:
    // uploads/_temp/2025/12/22/{token}/abc.png
    // uploads/products/2025/12/22/abc.png

    [StringLength(260)]
    public string? OriginalFileName { get; set; }

    [StringLength(100)]
    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    [StringLength(64)]
    public string? Sha256 { get; set; }

    // Temp upload flow
    public bool IsTemp { get; set; }

    [StringLength(80)]
    public string? TempToken { get; set; }

    /// <summary>
    /// Thời điểm hết hạn của file tạm, theo UTC.
    /// </summary>
    public DateTime? ExpireAtUtc { get; set; }

    public ICollection<ProductImage> ProductImages { get; set; }
        = new List<ProductImage>();
}