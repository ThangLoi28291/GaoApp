using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Products;

/// <summary>
/// DTO dùng để tra barcode từ controller/API.
/// </summary>
public class BarcodeLookupRequest
{
    [Required(ErrorMessage = "Barcode không được để trống.")]
    [StringLength(64)]
    public string Barcode { get; set; } = default!;
}