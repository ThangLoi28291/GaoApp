using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Taxes;

public sealed class TaxEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Mã")]
    public string? Code { get; set; } // create: cho phép trống (tự sinh)

    [Required(ErrorMessage = "Vui lòng nhập tên thuế")]
    [StringLength(200)]
    [Display(Name = "Tên")]
    public string Name { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập thuế suất")]
    [Range(0, 100, ErrorMessage = "Thuế suất phải nằm trong khoảng 0–100%.")]
    [Display(Name = "Thuế suất (%)")]
    public decimal Rate { get; set; }

    public bool Status { get; set; } = true;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
