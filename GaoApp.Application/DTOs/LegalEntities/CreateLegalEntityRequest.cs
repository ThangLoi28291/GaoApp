namespace GaoApp.Application.DTOs.LegalEntities;

/// <summary>
/// Request nền cho Phase 22.1. LegalEntity được tạo trước, sau đó kho thuộc
/// LegalEntity mới được tạo/gắn làm kho mặc định để tránh vòng FK khi insert.
/// </summary>
public sealed class CreateLegalEntityRequest
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LegalName { get; set; } = string.Empty;
    public string? TaxCode { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public int? InvoiceProviderSettingId { get; set; }
    public int SalePriority { get; set; } = 1;
    public bool IsDefaultForPurchase { get; set; }
    public string? Note { get; set; }
}
