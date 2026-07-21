namespace GaoApp.Application.DTOs.Suppliers;

public sealed class SupplierEditDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? ContactName { get; set; }
    public string? TaxCode { get; set; }
    public string? Note { get; set; }
    public bool Status { get; set; }
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}
