using System.ComponentModel.DataAnnotations.Schema;
using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

[Table("ProductVariantAttributeValue")]
public class ProductVariantAttributeValue : BaseStoreEntity
{
    public int VariantId { get; set; }
    public ProductVariant Variant { get; set; } = default!;

    public int AttributeId { get; set; }
    public ProductAttribute Attribute { get; set; } = default!;

    public int AttributeValueId { get; set; }
    public AttributeValue AttributeValue { get; set; } = default!;
}
