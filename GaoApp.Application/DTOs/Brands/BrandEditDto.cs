namespace GaoApp.Application.DTOs.Brands;

public sealed class BrandEditDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public bool Status { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
