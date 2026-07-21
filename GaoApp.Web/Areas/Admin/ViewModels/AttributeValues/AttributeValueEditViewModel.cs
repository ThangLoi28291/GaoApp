using System.ComponentModel.DataAnnotations;
using GaoApp.Domain.Entities;

namespace GaoApp.Web.Areas.Admin.ViewModels.AttributeValues;

public sealed class AttributeValueEditViewModel
{
    public int Id { get; set; }

    [Display(Name = "Thuộc tính")]
    [Range(1, int.MaxValue, ErrorMessage = "Vui lòng chọn thuộc tính")]
    public int AttributeId { get; set; }

    [Display(Name = "Mã")]
    public string? Code { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên giá trị")]
    [StringLength(200)]
    [Display(Name = "Tên")]
    public string Name { get; set; } = default!;

    public bool Status { get; set; } = true;
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public List<ProductAttribute> Attributes { get; set; } = new();
}
