using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Configuration;

/// <summary>
/// Bind từ section ConnectionStrings.
/// </summary>
public class ConnectionStringOptions
{
    public const string SectionName = "ConnectionStrings";

    /// <summary>
    /// Connection string chính của ứng dụng.
    /// </summary>
    [Required(ErrorMessage = "ConnectionStrings:DefaultConnection là bắt buộc.")]
    public string DefaultConnection { get; set; } = default!;
}