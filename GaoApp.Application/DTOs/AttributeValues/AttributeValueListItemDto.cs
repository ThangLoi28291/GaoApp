namespace GaoApp.Application.DTOs.AttributeValues;

public sealed class AttributeValueListItemDto
{
    public int Id { get; set; }
    public int AttributeId { get; set; }
    public string AttributeName { get; set; } = default!;

    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
}
