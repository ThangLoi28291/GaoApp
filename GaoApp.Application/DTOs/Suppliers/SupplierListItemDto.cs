namespace GaoApp.Application.DTOs.Suppliers;

public sealed class SupplierListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Phone { get; set; }
    public bool Status { get; set; } // map từ IsActive
}
