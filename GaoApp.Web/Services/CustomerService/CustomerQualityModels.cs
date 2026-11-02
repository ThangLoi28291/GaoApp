using System.Text.Json.Serialization;
namespace GaoApp.Web.Services.CustomerService;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerRating([property:JsonRequired] Guid RequestId,[property:JsonRequired] Guid MessageId,[property:JsonRequired] string? Kind);
public sealed record CustomerVote(Guid MessageId,string Kind);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerQualityReview([property:JsonRequired] Guid RequestId,[property:JsonRequired] string Version,[property:JsonRequired] string Action,string? Resolution);
public sealed record CustomerQualityCount(string Key,long Count);
public sealed record CustomerQualityReport(DateTimeOffset GeneratedAt,DateTimeOffset From,int RetentionDays,long Conversations,
    CustomerQualityCount[] Modes,CustomerQualityCount[] Sources,CustomerQualityCount[] Messages,CustomerQualityCount[] Handoffs,
    CustomerQualityCount[] Deliveries,long CustomerVotes,long HelpfulCustomerVotes,long StaffVotes,long OpenIssues,double? HelpfulCustomerRate);
public sealed record CustomerReviewItem(Guid Id,Guid ConversationId,Guid MessageId,string Source,string ActorType,string Kind,string Status,string Version,
    DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt,string? Resolution,int? ReviewedBy,string Question,string Reply);
public sealed record CustomerReviewPage(CustomerReviewItem[] Items,bool HasMore,int Page);
