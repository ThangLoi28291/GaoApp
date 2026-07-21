namespace GaoApp.Application.DTOs.ProductAttributes;

public sealed class ProductAttributeListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
}
