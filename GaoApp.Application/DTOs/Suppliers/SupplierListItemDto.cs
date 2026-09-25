namespace GaoApp.Application.DTOs.Suppliers;

public sealed class SupplierListItemDto
{
    public int Id { get; set; }
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? TaxCode { get; set; }
    public string? BankName { get; set; }
    public string? MaskedBankAccountNumber { get; set; }
    public bool Status { get; set; } // map từ IsActive
    public DateTime CreatedAtUtc { get; set; }
}
