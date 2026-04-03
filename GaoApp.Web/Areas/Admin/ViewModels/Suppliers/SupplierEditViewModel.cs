using System.ComponentModel.DataAnnotations;

namespace GaoApp.Web.Areas.Admin.ViewModels.Suppliers;

public sealed class SupplierEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Mã nhà cung cấp")]
    [StringLength(30, ErrorMessage = "Mã nhà cung cấp tối đa 30 ký tự")]
    public string? Code { get; set; } // Create được phép trống

    [Display(Name = "Tên nhà cung cấp")]
    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp tối đa 200 ký tự")]
    public string Name { get; set; } = default!;

    [Display(Name = "Số điện thoại")]
    [StringLength(30, ErrorMessage = "Số điện thoại tối đa 30 ký tự")]
    public string? Phone { get; set; }

    [Display(Name = "Email")]
    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(200, ErrorMessage = "Email tối đa 200 ký tự")]
    public string? Email { get; set; }

    [Display(Name = "Địa chỉ")]
    [StringLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự")]
    public string? Address { get; set; }

    [Display(Name = "Tên người liên hệ")]
    [StringLength(150, ErrorMessage = "Tên người liên hệ tối đa 150 ký tự")]
    public string? ContactName { get; set; }

    [Display(Name = "Mã số thuế")]
    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự")]
    public string? TaxCode { get; set; }

    [Display(Name = "Ghi chú")]
    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự")]
    public string? Note { get; set; }

    public bool Status { get; set; } = true;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
