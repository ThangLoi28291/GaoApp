namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class SetMultiLegalEntityEnabledRequest
{
    public bool IsEnabled { get; set; }
    public bool? ExpectedCurrentState { get; set; }
    public string? Reason { get; set; }
}
