using System.Text.Json.Serialization;

namespace GaoApp.Web.Services.CustomerService;

public sealed class CustomerServiceOptions
{
    public const string Section = "CustomerService";
    public string DatabasePath { get; set; } = "App_Data/Assistant/customer-service.db";
    public string StorageId { get; set; } = "";
    public int RetentionDays { get; set; } = 7;
    public int MaximumConversationsPerStore { get; set; } = 2000;
    public int MaximumMessagesPerConversation { get; set; } = 200;
    public int MaximumVisitorSessionsPerStore { get; set; } = 50000;
    public int MaximumVisitorSessionsTotal { get; set; } = 500000;
    public int VisitorSessionsPerMinutePerStore { get; set; } = 180;
    public int VisitorSessionsPerMinuteTotal { get; set; } = 1200;
    public int CleanupBatchSize { get; set; } = 100;
    public int CleanupIntervalSeconds { get; set; } = 15;
    public int CleanupBudgetMilliseconds { get; set; } = 250;
    public int StorageSlowMilliseconds { get; set; } = 500;
    public long StorageWarningBytes { get; set; } = 1024L*1024*1024;
    public long MaximumOutgoingMediaBytesPerStore { get; set; } = 200L*1024*1024;
    public long MaximumOutgoingMediaBytesTotal { get; set; } = 1024L*1024*1024;
    public bool Valid() => !string.IsNullOrWhiteSpace(DatabasePath) && RetentionDays is >= 1 and <= 7
        && MaximumConversationsPerStore is >= 10 and <= 10000 && MaximumMessagesPerConversation is >= 20 and <= 1000
        && MaximumVisitorSessionsPerStore is >=1 and <=1000000 && MaximumVisitorSessionsTotal>=MaximumVisitorSessionsPerStore && MaximumVisitorSessionsTotal<=5000000
        && VisitorSessionsPerMinutePerStore is >=1 and <=10000 && VisitorSessionsPerMinuteTotal>=VisitorSessionsPerMinutePerStore && VisitorSessionsPerMinuteTotal<=100000
        && CleanupBatchSize is >=1 and <=1000 && CleanupIntervalSeconds is >=1 and <=300 && CleanupBudgetMilliseconds is >=25 and <=2000
        && StorageSlowMilliseconds is >=1 and <=30000 && StorageWarningBytes is >=1048576 and <=107374182400
        && MaximumOutgoingMediaBytesPerStore is >= 20971520 and <= 10737418240
        && MaximumOutgoingMediaBytesTotal >= MaximumOutgoingMediaBytesPerStore && MaximumOutgoingMediaBytesTotal <= 53687091200;
}
public static class CustomerServiceRights
{
    public const string View = "customer.service.view", Reply = "customer.service.reply", Manage = "customer.service.manage", Verify = "customer.service.verify";
}
public sealed record CustomerStaff(int UserId, int StoreId, string Name, bool CanReply, bool CanManage, bool CanVerify = false);
public sealed record CustomerTicket(Guid VisitorId, int StoreId, DateTimeOffset ExpiresAt,int Version=1);
public sealed record CustomerStoreSettings(bool Enabled, long Version);
public sealed record CustomerMessage(Guid Id, string Role, string Text, DateTimeOffset CreatedAt, string? AgentName, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CustomerChannelDelivery? Delivery=null, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CustomerZaloMessage? Zalo=null, [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CustomerOutgoingAttachment? Outgoing=null);
public sealed record CustomerConversation(Guid Id, int StoreId, Guid VisitorId, string Name, string? Contact, string Mode,
    int? Owner, string? OwnerName, long Version, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset ExpiresAt,
    int Unread, string? HandoffReason, CustomerMessage[] Messages, string Source="web", string ConversationKind="Direct", [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] CustomerZaloHistoryItem[]? ZaloHistory=null);
public sealed record CustomerTurn(CustomerConversation Conversation, bool RunAi, string StateJson, Guid RequestId);
public sealed record CustomerBridgeIdentity(Guid VisitorId, int StoreId, Guid ConversationId, Guid TurnId);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerStart([property:JsonRequired] Guid RequestId, [property:JsonRequired] string Name, string? Contact, [property:JsonRequired] bool Consent);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerSend([property:JsonRequired] Guid RequestId, [property:JsonRequired] string Message);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerCommand([property:JsonRequired] Guid RequestId, [property:JsonRequired] string Action, [property:JsonRequired] long Version, string? Message = null);
