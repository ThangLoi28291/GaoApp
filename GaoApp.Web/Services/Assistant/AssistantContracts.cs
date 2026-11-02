using System.Text.Json.Serialization;

namespace GaoApp.Web.Services.Assistant;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AssistantScope([property: JsonRequired] string StoreId, [property: JsonRequired] string? WarehouseId, [property: JsonRequired] string? LegalEntityId);
public sealed record AssistantIdentity(int UserId, string DisplayName, AssistantScope Scope, string[] Permissions);
public sealed record AssistantSource(string Kind, string Label, string Revision, DateTimeOffset VerifiedAt, bool IsTest = false);
public sealed record CatalogProduct(string ProductId, string VariantId, string Name);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CatalogAttributes
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Family { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Brand { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Sugar { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Capacity { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Flavor { get; init; }
}
public sealed record CatalogUnit(string UnitId, string? ConversionId, string Name, string Factor,
    bool IsBaseUnit, bool CanReceive, bool CanSell);
public sealed record CatalogPrice(string Status, string? Amount, string? ErrorCode = null);
public sealed record CatalogSaleUnit(CatalogUnit Unit, CatalogPrice Retail);
public sealed record CatalogVariant(CatalogProduct Product, CatalogAttributes Attributes,
    CatalogUnit BaseUnit, CatalogSaleUnit[] Units, bool Active);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CatalogAliasQuery { public string Query { get; set; } = ""; }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class CatalogSearch
{
    public string Query { get; set; } = "";
    public CatalogAttributes? Attributes { get; set; }
    public string[]? ContextVariantIds { get; set; }
    public int Limit { get; set; } = 20;
    public string? Cursor { get; set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteItem(string ProductId, string VariantId, string UnitId, string? ConversionId = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class QuoteLookup
{
    public QuoteItem[] Items { get; set; } = [];
    public string[] Tiers { get; set; } = ["retail"];
}
public sealed record QuotedPrice(string RequestedTier, string? AppliedTier, string Status, string? Amount,
    string? FallbackReason = null, string? ErrorCode = null);
public sealed record QuotedItem(CatalogProduct Product, CatalogUnit Unit, string Currency, QuotedPrice[] Prices);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class AuthorizationRequest { public string[] RequiredPermissions { get; set; } = []; }
public sealed class AssistantException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public object Body(Guid id) => new { contractVersion = "1.0", requestId = id, code = Code,
        message = Message, retryable = Status is 502 or 503 or 504 };
    public static AssistantException Invalid() => new(400, "invalid_request", "Yêu cầu trợ lý không hợp lệ.");
    public static AssistantException Denied() => new(403, "permission_denied", "Bạn chưa có quyền thực hiện thao tác này.");
    public static AssistantException Disabled() => new(503, "assistant_disabled", "Trợ lý đang tạm tắt. Bạn vẫn có thể thao tác thủ công.");
}
