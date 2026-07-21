namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityDto
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
    public string? DefaultWarehouseName { get; set; }
    public string? DefaultWarehouseCode { get; set; }
    public int? InvoiceProviderSettingId { get; set; }
    public string? InvoiceSupplierTaxCode { get; set; }
    public string? InvoiceProviderCode { get; set; }
    public bool? IsInvoiceSettingActive { get; set; }
    public int SalePriority { get; set; }
    public bool IsDefaultForPurchase { get; set; }
    public bool IsActive { get; set; }
    public int WarehouseCount { get; set; }
    public int ActiveWarehouseCount { get; set; }
    public string? Note { get; set; }
}
