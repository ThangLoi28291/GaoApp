using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Taxes;

public sealed class UpdateTaxRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã thuế")]
    [StringLength(30)]
    public string Code { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập tên thuế")]
    [StringLength(200)]
    public string Name { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập thuế suất")]
    [Range(0, 100, ErrorMessage = "Thuế suất phải nằm trong khoảng 0–100%.")]
    public decimal Rate { get; set; }

    public bool Status { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
