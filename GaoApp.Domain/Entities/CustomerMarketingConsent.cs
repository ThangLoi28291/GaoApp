using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Current marketing permission for one customer and channel.</summary>
public sealed class CustomerMarketingConsent : BaseStoreEntity
{
    public int CustomerId { get; set; }
    public string Channel { get; set; } = "Zalo";
    public bool IsGranted { get; set; }
    public string Source { get; set; } = "";
    public string? EvidenceReference { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public int? ChangedByUserId { get; set; }
    public Customer? Customer { get; set; }
}

/// <summary>Append-only evidence of marketing consent changes.</summary>
public sealed class CustomerMarketingConsentEvent : BaseStoreEntity
{
    public int CustomerId { get; set; }
    public string Channel { get; set; } = "Zalo";
    public bool IsGranted { get; set; }
    public string Source { get; set; } = "";
    public string? EvidenceReference { get; set; }
    public DateTime RecordedAtUtc { get; set; }
    public int? RecordedByUserId { get; set; }
}
