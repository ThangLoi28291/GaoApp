using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Brands;

public sealed class BrandEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Mã thương hiệu")]
    [StringLength(30, ErrorMessage = "Mã thương hiệu tối đa 30 ký tự")]
    public string? Code { get; set; }

    [Display(Name = "Tên thương hiệu")]
    [Required(ErrorMessage = "Vui lòng nhập tên thương hiệu")]
    [StringLength(200, ErrorMessage = "Tên thương hiệu tối đa 200 ký tự")]
    public string Name { get; set; } = default!;

    [Display(Name = "Mô tả")]
    [StringLength(300, ErrorMessage = "Mô tả tối đa 300 ký tự")]
    public string? Description { get; set; }

    public bool Status { get; set; } = true;
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
