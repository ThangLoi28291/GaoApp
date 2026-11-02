using GaoApp.Domain.Common;

namespace GaoApp.Domain.Entities;

/// <summary>Immutable audience and content snapshot for one consent-based Zalo campaign.</summary>
public sealed class CustomerMarketingCampaign : BaseStoreEntity
{
    public Guid CampaignKey { get; set; }
    public Guid PreviewRequestId { get; set; }
    public string RequestFingerprint { get; set; } = "";
    public string Name { get; set; } = "";
    public string MessageText { get; set; } = "";
    public string? AssetKind { get; set; }
    public string? AssetFileName { get; set; }
    public string? AssetContentType { get; set; }
    public long? AssetLength { get; set; }
    public string? AssetSha256 { get; set; }
    public byte[]? AssetBytes { get; set; }
    public string? CustomerGroup { get; set; }
    public string ConnectionId { get; set; } = "";
    public string ConnectionBinding { get; set; } = "";
    public Guid SenderProfileId { get; set; }
    public string SnapshotSha256 { get; set; } = "";
    public int RecipientCount { get; set; }
    public DateTime ScheduledAtUtc { get; set; }
    public string Status { get; set; } = "Draft";
    public int CreatedByUserId { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Detail { get; set; }
}

/// <summary>Recipient identity captured at preview time and revalidated before every dispatch.</summary>
public sealed class CustomerMarketingCampaignRecipient : BaseStoreEntity
{
    public int CampaignId { get; set; }
    public int CustomerId { get; set; }
    public string CustomerNameSnapshot { get; set; } = "";
    public Guid BindingId { get; set; }
    public int BindingVersion { get; set; }
    public string ConnectionId { get; set; } = "";
    public string ConnectionBinding { get; set; } = "";
    public Guid SenderProfileId { get; set; }
    public Guid ConversationId { get; set; }
    public string NativePeer { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Status { get; set; } = "Pending";
    public Guid? ZaloJobId { get; set; }
    public int Attempts { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public string? Detail { get; set; }
}
