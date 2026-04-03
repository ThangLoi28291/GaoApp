using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.Common.Options;

/// <summary>
/// Cấu hình các URL gốc của hệ thống.
/// Dùng cho redirect, generate link, callback URL...
/// </summary>
public class AppUrlOptions
{
    public const string SectionName = "AppUrl";

    /// <summary>
    /// URL gốc public của web app.
    /// Ví dụ: https://admin.gaomart.com.vn
    /// </summary>
    [Required(ErrorMessage = "AppUrl:BaseUrl là bắt buộc.")]
    [Url(ErrorMessage = "AppUrl:BaseUrl phải là URL hợp lệ.")]
    public string BaseUrl { get; set; } = default!;

    /// <summary>
    /// URL gốc dành cho admin nếu có tách riêng.
    /// Có thể dùng chung BaseUrl nếu chưa tách.
    /// </summary>
    [Url(ErrorMessage = "AppUrl:AdminUrl phải là URL hợp lệ.")]
    public string? AdminUrl { get; set; }
}