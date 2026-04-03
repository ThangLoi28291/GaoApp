namespace GaoApp.Application.DTOs.ProductAttributes;

public sealed class CreateProductAttributeRequest
{
    public string? Code { get; set; }   // cho phép trống -> tự sinh
    public string Name { get; set; } = default!;
    public bool Status { get; set; } = true;
}
