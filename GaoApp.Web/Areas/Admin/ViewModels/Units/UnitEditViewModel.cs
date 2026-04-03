using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Units;

public sealed class UnitEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Mã")]
    [StringLength(30, ErrorMessage = "Mã tối đa 30 ký tự")]
    public string? Code { get; set; } // create: có thể trống -> tự sinh

    [Required(ErrorMessage = "Vui lòng nhập tên đơn vị")]
    [StringLength(200, ErrorMessage = "Tên tối đa 200 ký tự")]
    [Display(Name = "Tên")]
    public string Name { get; set; } = default!;

    [Display(Name = "Đơn vị gốc")]
    public bool IsBase { get; set; }

    [Display(Name = "Thứ tự")]
    [Range(0, 999999, ErrorMessage = "Thứ tự không hợp lệ")]
    public int SortOrder { get; set; } = 0;

    [Display(Name = "Trạng thái")]
    public bool Status { get; set; } = true;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
