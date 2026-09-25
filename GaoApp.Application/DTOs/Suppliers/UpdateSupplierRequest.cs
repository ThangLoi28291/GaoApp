using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Suppliers;

public sealed class UpdateSupplierRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã nhà cung cấp")]
    [StringLength(30, ErrorMessage = "Mã nhà cung cấp tối đa 30 ký tự")]
    public string Code { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập tên nhà cung cấp")]
    [StringLength(200, ErrorMessage = "Tên nhà cung cấp tối đa 200 ký tự")]
    public string Name { get; set; } = default!;

    [StringLength(30, ErrorMessage = "Số điện thoại tối đa 30 ký tự")]
    public string? Phone { get; set; }

    [EmailAddress(ErrorMessage = "Email không đúng định dạng")]
    [StringLength(200, ErrorMessage = "Email tối đa 200 ký tự")]
    public string? Email { get; set; }

    [StringLength(300, ErrorMessage = "Địa chỉ tối đa 300 ký tự")]
    public string? Address { get; set; }

    [StringLength(150, ErrorMessage = "Tên người liên hệ tối đa 150 ký tự")]
    public string? ContactName { get; set; }

    [StringLength(50, ErrorMessage = "Mã số thuế tối đa 50 ký tự")]
    public string? TaxCode { get; set; }

    [StringLength(50, ErrorMessage = "Số tài khoản tối đa 50 ký tự")]
    public string? BankAccountNumber { get; set; }

    [StringLength(250, ErrorMessage = "Tên chủ tài khoản tối đa 250 ký tự")]
    public string? BankAccountName { get; set; }

    [StringLength(250, ErrorMessage = "Tên ngân hàng tối đa 250 ký tự")]
    public string? BankName { get; set; }

    [StringLength(500, ErrorMessage = "Ghi chú tối đa 500 ký tự")]
    public string? Note { get; set; }

    public bool Status { get; set; } = true;

    [Required]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
