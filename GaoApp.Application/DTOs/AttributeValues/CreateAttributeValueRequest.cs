namespace GaoApp.Application.DTOs.AttributeValues;

public sealed class CreateAttributeValueRequest
{
    public int AttributeId { get; set; }
    public string? Code { get; set; } // cho phép trống -> tự sinh
    public string Name { get; set; } = default!;
    public bool Status { get; set; } = true;
}
