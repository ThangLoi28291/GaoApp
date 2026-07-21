using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("Attribute")]
public class ProductAttribute : BaseStoreEntity
{
    [Required, StringLength(30)]
    public string Code { get; set; } = default!; // VD: COLOR, SIZE, FLAVOR

    [Required, StringLength(200)]
    public string Name { get; set; } = default!; // VD: Màu sắc, Kích cỡ

    public bool Status { get; set; } = true;     // dùng đúng style template bạn đang có
    public int SortOrder { get; set; } = 0;

    public ICollection<AttributeValue> Values { get; set; } = new List<AttributeValue>();
}
