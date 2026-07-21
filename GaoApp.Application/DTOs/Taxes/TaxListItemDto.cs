namespace GaoApp.Application.DTOs.Taxes;

public sealed class TaxListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public decimal Rate { get; set; }
    public bool Status { get; set; } // map IsActive
}
