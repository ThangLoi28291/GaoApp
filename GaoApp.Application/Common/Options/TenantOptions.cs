using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.Common.Options;

/// <summary>
/// Cấu hình tenant / multi-tenant theo subdomain.
/// </summary>
public class TenantOptions
{
    public const string SectionName = "Tenant";

    /// <summary>
    /// Root domain hệ thống.
    /// Ví dụ: gaomart.com.vn
    /// </summary>
    [Required(ErrorMessage = "Tenant:RootDomain là bắt buộc.")]
    public string RootDomain { get; set; } = default!;

    /// <summary>
    /// Subdomain admin mặc định.
    /// Ví dụ: admin
    /// </summary>
    [Required(ErrorMessage = "Tenant:AdminSubdomain là bắt buộc.")]
    public string AdminSubdomain { get; set; } = "admin";
}