using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.Common.Options;

/// <summary>
/// Cấu hình lưu trữ file local.
/// Sau này có thể mở rộng sang S3, MinIO, Azure Blob...
/// </summary>
public class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// Thư mục root chứa upload.
    /// Ví dụ: wwwroot/uploads
    /// </summary>
    [Required(ErrorMessage = "Storage:UploadRoot là bắt buộc.")]
    public string UploadRoot { get; set; } = default!;

    /// <summary>
    /// Có cho phép tạo thư mục tự động khi app start hay không.
    /// </summary>
    public bool CreateIfMissing { get; set; } = true;
}