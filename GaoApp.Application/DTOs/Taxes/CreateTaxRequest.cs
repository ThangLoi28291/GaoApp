using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Taxes;

public sealed class CreateTaxRequest
{
    [StringLength(30)]
    public string? Code { get; set; } // cho phép trống -> service tự sinh như Supplier :contentReference[oaicite:5]{index=5}

    [Required(ErrorMessage = "Vui lòng nhập tên thuế")]
    [StringLength(200)]
    public string Name { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập thuế suất")]
    [Range(0, 100, ErrorMessage = "Thuế suất phải nằm trong khoảng 0–100%.")]
    public decimal Rate { get; set; }

    public bool Status { get; set; } = true;
}
