namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityOptionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int SalePriority { get; set; }
    public bool IsDefaultForPurchase { get; set; }
}
