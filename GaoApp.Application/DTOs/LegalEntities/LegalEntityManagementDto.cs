namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityManagementDto
{
    public bool IsMultiLegalEntityEnabled { get; set; }
    public DateTime? MultiLegalEntityActivatedAtUtc { get; set; }
    public List<LegalEntityDto> LegalEntities { get; set; } = new();
    public List<LegalEntityWarehouseOptionDto> Warehouses { get; set; } = new();
    public List<LegalEntityInvoiceSettingOptionDto> InvoiceSettings { get; set; } = new();
}

public sealed class LegalEntityWarehouseOptionDto
{
    public int Id { get; set; }
    public int LegalEntityId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool AllowNegativeInventory { get; set; }
}

public sealed class LegalEntityInvoiceSettingOptionDto
{
    public int Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public string SupplierTaxCode { get; set; } = string.Empty;
    public string TemplateCode { get; set; } = string.Empty;
    public string InvoiceSeries { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}
