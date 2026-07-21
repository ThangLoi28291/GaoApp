namespace GaoApp.Application.DTOs.AttributeValues;

public sealed class AttributeValueEditDto
{
    public int Id { get; set; }
    public int AttributeId { get; set; }

    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public bool Status { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    public int? UpdatedBy { get; set; }

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
