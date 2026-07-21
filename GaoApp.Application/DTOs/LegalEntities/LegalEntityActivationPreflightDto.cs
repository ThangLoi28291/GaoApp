namespace GaoApp.Application.DTOs.LegalEntities;

public sealed class LegalEntityActivationPreflightDto
{
    public bool IsFeatureEnabled { get; set; }
    public DateTime? ActivatedAtUtc { get; set; }
    public int ActiveLegalEntityCount { get; set; }
    public bool IsConfigurationReady { get; set; }

    /// <summary>
    /// Chỉ cho phép bật feature khi toàn bộ preflight cấu hình đã đạt.
    /// </summary>
    public bool CanActivate { get; set; }
    public string ActivationGateMessage { get; set; } = string.Empty;
    public List<LegalEntityPreflightCheckDto> Checks { get; set; } = new();
}

public sealed class LegalEntityPreflightCheckDto
{
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public bool IsPassed { get; set; }
    public string Level { get; set; } = "Error";
    public string Message { get; set; } = string.Empty;
    public int? LegalEntityId { get; set; }
}
