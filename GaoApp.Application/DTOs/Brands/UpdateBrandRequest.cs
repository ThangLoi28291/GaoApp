using System.ComponentModel.DataAnnotations;

namespace GaoApp.Application.DTOs.Brands;

public sealed class UpdateBrandRequest
{
    public int Id { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập mã thương hiệu")]
    [StringLength(30, ErrorMessage = "Mã thương hiệu tối đa 30 ký tự")]
    public string Code { get; set; } = default!;

    [Required(ErrorMessage = "Vui lòng nhập tên thương hiệu")]
    [StringLength(200, ErrorMessage = "Tên thương hiệu tối đa 200 ký tự")]
    public string Name { get; set; } = default!;

    [StringLength(300, ErrorMessage = "Mô tả tối đa 300 ký tự")]
    public string? Description { get; set; }

    public bool Status { get; set; } = true;

    [Required]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
