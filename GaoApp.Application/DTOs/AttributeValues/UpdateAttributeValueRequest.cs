namespace GaoApp.Application.DTOs.AttributeValues;

public sealed class UpdateAttributeValueRequest
{
    public int Id { get; set; }
    public int AttributeId { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
