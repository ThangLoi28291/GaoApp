namespace GaoApp.Application.DTOs.Taxes;

public sealed class TaxEditDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public decimal Rate { get; set; }
    public bool Status { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>(); // giống Supplier :contentReference[oaicite:6]{index=6}
}
