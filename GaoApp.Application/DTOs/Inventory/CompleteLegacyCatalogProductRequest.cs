using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Inventory;

public sealed class CompleteLegacyCatalogProductRequest
{
    [Required]
    [StringLength(200, MinimumLength = 2)]
    public string ProductName { get; set; } = string.Empty;

    [Required]
    public string RowVersion { get; set; } = string.Empty;
}
