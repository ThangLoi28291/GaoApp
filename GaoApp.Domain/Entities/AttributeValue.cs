using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("AttributeValue")]
public class AttributeValue : BaseStoreEntity
{
    public int AttributeId { get; set; }

    [ForeignKey(nameof(AttributeId))]
    public ProductAttribute Attribute { get; set; } = default!; // ✅ đổi sang ProductAttribute

    [Required, StringLength(30)]
    public string Code { get; set; } = default!;

    [Required, StringLength(200)]
    public string Name { get; set; } = default!;

    public bool Status { get; set; } = true;
    public int SortOrder { get; set; } = 0;
}
