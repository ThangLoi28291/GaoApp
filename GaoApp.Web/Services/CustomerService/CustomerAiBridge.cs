using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerAiResult(string Reply,string State,bool NeedsHuman,string PolicyVersion);
public sealed class CustomerAiBridge(HttpClient http,AssistantJwt jwt,IOptionsMonitor<AssistantOptions> options,IWebHostEnvironment environment,
    AssistantCatalogService catalog,AssistantAvailabilityService availability,AssistantPolicyKnowledge policies,TimeProvider clock)
{
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web);
    private sealed record CustomerAiSources(object Payload,string PolicyVersion);
    public async Task<CustomerAiResult> Reply(CustomerBridgeIdentity who,string message,string state,CancellationToken ct) {
        var config=options.CurrentValue;
        if(!config.Enabled)throw AssistantException.Disabled();
        if(!Uri.TryCreate(config.BaseUrl,UriKind.Absolute,out var origin)||origin.UserInfo!=""||origin.Query!=""||origin.Fragment!=""
            ||origin.Scheme!="https"&&!(environment.IsDevelopment()&&origin.Scheme=="http"&&origin.IsLoopback))throw AssistantException.Disabled();
        var sources=await SourcesAsync(who,message,ct);
        var body=new{requestId=who.TurnId,conversationId=who.ConversationId,message,state=string.IsNullOrEmpty(state)?(JsonElement?)null:JsonSerializer.Deserialize<JsonElement>(state),sources=sources.Payload};
        var bytes=JsonSerializer.SerializeToUtf8Bytes(body,Wire);
        if(bytes.Length>512*1024)throw new AssistantException(503,"customer_ai_context_too_large","Dữ liệu tư vấn vượt giới hạn an toàn.");
        var hash=Convert.ToBase64String(SHA256.HashData(bytes)).TrimEnd('=').Replace('+','-').Replace('/','_');
        var tokens=jwt.SignCustomer(who,hash);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds,5,100)));
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(new Uri(origin.AbsoluteUri.TrimEnd('/')+"/"),"api/customer/v1/messages"));
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",tokens.Context);request.Headers.Add("X-GaoApp-Delegation",tokens.Delegation);request.Headers.Add("X-Request-Id",who.TurnId.ToString());
        request.Content=new ByteArrayContent(bytes);request.Content.Headers.ContentType=new("application/json");
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024*1024).WaitAsync(deadline.Token);
        if(!options.CurrentValue.Enabled)throw AssistantException.Disabled();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(deadline.Token));AssistantJwt.Unique(json.RootElement);var value=json.RootElement;
        if(value.GetProperty("contractVersion").GetString()!="1.0"||value.GetProperty("requestId").GetGuid()!=who.TurnId||value.GetProperty("conversationId").GetGuid()!=who.ConversationId
            ||value.GetProperty("scope").Deserialize<AssistantScope>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!=new AssistantScope(who.StoreId.ToString(),null,null))throw new JsonException();
        var reply=value.GetProperty("reply").GetString();var context=value.GetProperty("state");var next=context.GetRawText();
        if(string.IsNullOrWhiteSpace(reply)||reply.Length>24000||next.Length>262144||context.GetProperty("conversationId").GetGuid()!=who.ConversationId)throw new JsonException();
        return new(reply,next,value.TryGetProperty("needsHuman",out var needs)&&needs.GetBoolean(),sources.PolicyVersion);
    }
    public async Task<bool> PolicyVersionCurrentAsync(int storeId,string expected,CancellationToken ct) =>
        options.CurrentValue.Enabled&&string.Equals((await policies.ReadAsync(storeId,ct)).Version,expected,StringComparison.Ordinal);
    private async Task<CustomerAiSources> SourcesAsync(CustomerBridgeIdentity who,string message,CancellationToken ct) {
        var identity=new AssistantIdentity(0,"Khách",new(who.StoreId.ToString(),null,null),
            [AssistantPermissions.Use,AssistantPermissions.Chat,AssistantPermissions.Catalog,AssistantPermissions.Retail]);
        var found=await catalog.SearchAsync(identity,new CatalogSearch{Query=message,Limit=3},ct);
        var products=new List<object>();
        foreach(var variant in found.Variants.Take(3)) {
            var unit=variant.Units.FirstOrDefault(u=>u.Unit.CanSell&&u.Unit.IsBaseUnit)
                ??variant.Units.FirstOrDefault(u=>u.Unit.CanSell);
            if(unit is null)continue;
            var quote=(await catalog.QuoteAsync(identity,new QuoteLookup{Items=[new(variant.Product.ProductId,variant.Product.VariantId,unit.Unit.UnitId,unit.Unit.ConversionId)],Tiers=["retail"]},ct)).Single();
            var stock=await availability.ReadAsync(identity,variant,ct);
            products.Add(new{product=variant.Product,attributes=variant.Attributes,quote,availability=stock});
        }
        var now=clock.GetUtcNow();
        var policy=await policies.ReadAsync(who.StoreId,ct);
        var articles=AssistantPolicyKnowledge.Topics.Select(topic=>AssistantPolicyKnowledge.Current(policy,topic,now))
            .Where(row=>row is not null).Select(row=>new{row!.Topic,row.Title,row.Body,row.EffectiveFrom,row.EffectiveUntil}).ToArray();
        return new(new{revision=catalog.Source().Revision+":"+policy.Version,verifiedAt=now,products,policies=articles},policy.Version);
    }
    public static string? Handoff(string message) {
        var text=AssistantCatalogSearch.Canonical(message);
        return System.Text.RegularExpressions.Regex.IsMatch(text,@"\b(?:gap nhan vien|nguoi that|tu van vien|khieu nai|khong hai long|gia si|gia buon|diem cua toi|don hang cua toi|kiem tra don|tra cuu (?:don|diem)|chuyen khoan roi|(?:toi|minh|em) (?:muon|can) (?:doi hang|tra hang|hoan tien)|(?:toi|minh|em) (?:co |con )?bao nhieu diem)\b")?"customer_request":null;
    }
    public static bool ExplicitHandoff(string message)=>System.Text.RegularExpressions.Regex.IsMatch(AssistantCatalogSearch.Canonical(message),@"\b(?:gap nhan vien|nguoi that|tu van vien|khieu nai|khong hai long|gia si|gia buon|chuyen khoan roi|(?:toi|minh|em) (?:muon|can) (?:doi hang|tra hang|hoan tien))\b");
}
