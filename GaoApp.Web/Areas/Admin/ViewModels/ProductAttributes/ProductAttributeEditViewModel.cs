using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.ProductAttributes;

public sealed class ProductAttributeEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Mã")]
    [StringLength(30, ErrorMessage = "Mã tối đa 30 ký tự")]
    public string? Code { get; set; } // Create: cho phép trống -> tự sinh (giống Supplier)

    [Required(ErrorMessage = "Vui lòng nhập tên thuộc tính")]
    [StringLength(200, ErrorMessage = "Tên tối đa 200 ký tự")]
    [Display(Name = "Tên")]
    public string Name { get; set; } = default!;

    [Display(Name = "Trạng thái")]
    public bool Status { get; set; } = true;

    // Nếu entity ProductAttribute của bạn CHƯA có RowVersion thì XÓA 1 dòng này
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
