namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class UpdateLegalEntityRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int? DefaultWarehouseId { get; set; }
    public int? InvoiceProviderSettingId { get; set; }
    public int SalePriority { get; set; }
    public bool IsDefaultForPurchase { get; set; }
    public string? Note { get; set; }
}
