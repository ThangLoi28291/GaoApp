using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Controllers;

[ApiController,AllowAnonymous,IgnoreAntiforgeryToken,Route("api/v1/customer-assistant"),RequestSizeLimit(65536)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerAssistantSourceController(AssistantJwt jwt,CustomerServiceStore store,AssistantCatalogService catalog,AssistantAvailabilityService availability,AssistantPolicyKnowledge policies,
    AssistantStoreSettings switches,GaoApp.Infrastructure.Data.AppDbContext db,TimeProvider clock,ResourceAdmission admission,IOptionsMonitor<AssistantOptions> options):ControllerBase,IAsyncActionFilter
{
    private AssistantIdentity who=null!;
    private CustomerBridgeIdentity customer=null!;
    private Guid request;
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private async Task Current(CancellationToken ct) {
        if(!options.CurrentValue.Enabled||!store.BridgeAllowed(customer)||!await db.Stores.IgnoreQueryFilters().AnyAsync(s=>s.Id==customer.StoreId&&s.IsActive&&!s.IsDeleted,ct))throw AssistantException.Denied();
        var settings=await switches.ReadAsync(customer.StoreId,ct);if(!settings.Enabled||!settings.ChatEnabled)throw AssistantException.Denied();
    }
    // This is the controller's authorization filter, not an API action. MVC would
    // infer two body parameters and fail all route initialization without NonAction.
    [NonAction]
    public async Task OnActionExecutionAsync(ActionExecutingContext action,ActionExecutionDelegate next) {
        try {
            if(Request.Headers.Authorization.Count!=1||!Request.Headers.Authorization.ToString().StartsWith("Bearer ",StringComparison.Ordinal)
                ||Request.Headers["X-Request-Id"].Count!=1||!Guid.TryParse(Request.Headers["X-Request-Id"],out request)||request==Guid.Empty)throw new AssistantException(401,"unauthenticated","Thiếu kết nối được xác thực.");
            customer=jwt.ValidateCustomer(Request.Headers.Authorization.ToString()[7..]);await Current(HttpContext.RequestAborted);
            who=new(0,"Khách",new(customer.StoreId.ToString(),null,null),[AssistantPermissions.Use,AssistantPermissions.Chat,AssistantPermissions.Catalog,AssistantPermissions.Retail]);
            using var lease=admission.Enter(ResourceGroup.AssistantSource,ResourceSubject.Visitor(customer.VisitorId),customer.StoreId);
            if(!lease.IsAcquired){action.Result=ResourceProtectionResponse.Result(HttpContext,lease,request);return;}
            foreach(var value in action.ActionArguments.Values.OfType<JsonElement>())AssistantJwt.Unique(value);
            if(!ModelState.IsValid)throw AssistantException.Invalid();var result=await next();
            if(result.Exception is AssistantException error){result.ExceptionHandled=true;result.Result=StatusCode(error.Status,error.Body(request));}
            else if(result.Exception is JsonException or FormatException){result.ExceptionHandled=true;result.Result=BadRequest(AssistantException.Invalid().Body(request));}
            else if(result.Exception is not null){result.ExceptionHandled=true;result.Result=StatusCode(503,new{code="customer_source_unavailable",message="Chưa đọc được dữ liệu cửa hàng."});}
            else { try { await Current(HttpContext.RequestAborted); } catch(AssistantException e) { result.Result=StatusCode(e.Status,e.Body(request)); } }
        }catch(AssistantException e){action.Result=StatusCode(e.Status,e.Body(request));}
        catch(JsonException){action.Result=BadRequest(AssistantException.Invalid().Body(request));}
    }
    [HttpPost("context/authorize")]
    public IActionResult Authorize([FromBody]JsonElement body) {
        var input=body.Deserialize<AuthorizationRequest>(Json)??throw AssistantException.Invalid();
        if(input.RequiredPermissions is null||input.RequiredPermissions.Length is <1 or >4||input.RequiredPermissions.Distinct().Count()!=input.RequiredPermissions.Length)throw AssistantException.Invalid();
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,allowed=input.RequiredPermissions.All(who.Permissions.Contains),permissions=who.Permissions,permissionVersion="customer-retail-v1",verifiedAt=clock.GetUtcNow()});
    }
    [HttpPost("catalog/search")]
    public async Task<IActionResult> Search([FromBody]JsonElement body,CancellationToken ct) {
        var input=body.Deserialize<CatalogSearch>(Json)??throw AssistantException.Invalid();var result=await catalog.SearchAsync(who,input,ct);
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,source=catalog.Source(),variants=result.Variants.Where(v=>v.Units.Any(u=>u.Unit.CanSell)).ToArray(),nextCursor=result.Cursor,search=result.Search});
    }
    [HttpPost("catalog/aliases/resolve")]
    public async Task<IActionResult> Alias([FromBody]JsonElement body,CancellationToken ct) {
        var input=body.Deserialize<CatalogAliasQuery>(Json)??throw AssistantException.Invalid();var query=await catalog.ApprovedAliasAsync(who,input.Query,ct);
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,matched=query is not null,query});
    }
    [HttpGet("catalog/variants/{variantId}")]
    public async Task<IActionResult> Variant(string variantId,CancellationToken ct) {
        var variant=AssistantCatalogService.Build(await catalog.FindAsync(who,variantId,ct),true);
        if(!variant.Units.Any(u=>u.Unit.CanSell))throw new AssistantException(404,"product_not_found","Không tìm thấy mặt hàng đang bán.");
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,source=catalog.Source(),variant});
    }
    [HttpGet("catalog/variants/{variantId}/availability")]
    public async Task<IActionResult> Availability(string variantId,CancellationToken ct) {
        var variant=AssistantCatalogService.Build(await catalog.FindAsync(who,variantId,ct),true);
        if(!variant.Units.Any(u=>u.Unit.CanSell))throw new AssistantException(404,"product_not_found","Không tìm thấy mặt hàng đang bán.");
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,availability=await availability.ReadAsync(who,variant,ct)});
    }
    [HttpPost("quotes/lookup")]
    public async Task<IActionResult> Quotes([FromBody]JsonElement body,CancellationToken ct) {
        var input=body.Deserialize<QuoteLookup>(Json)??throw AssistantException.Invalid();if(input.Tiers is null||input.Tiers.Length!=1||input.Tiers[0]!="retail")throw AssistantException.Denied();
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,source=catalog.Source(),quotes=await catalog.QuoteAsync(who,input,ct)});
    }
    [HttpPost("knowledge/lookup")]
    public async Task<IActionResult> Knowledge([FromBody]JsonElement body,CancellationToken ct) {
        var input=body.Deserialize<PolicyLookup>(Json)??throw AssistantException.Invalid();if(input.Topics is null||input.Topics.Length is <1 or >6||input.Topics.Distinct().Count()!=input.Topics.Length||input.Topics.Any(t=>!AssistantPolicyKnowledge.Topics.Contains(t)))throw AssistantException.Invalid();
        var doc=await policies.ReadAsync(customer.StoreId,ct);var now=clock.GetUtcNow();
        var articles=input.Topics.Select(t=>AssistantPolicyKnowledge.Current(doc,t,now)).Where(r=>r is not null).Select(r=>new{revisionId=r!.Id,r.Topic,r.Title,r.Body,r.EffectiveFrom,effectiveUntil=End(doc,r)}).ToArray();
        return Ok(new{contractVersion="1.0",requestId=request,scope=who.Scope,source=new{kind="gaoapp",label="Chính sách đã duyệt",revision=doc.Version,verifiedAt=now,isTest=false},articles});
    }
    private static DateTimeOffset? End(PolicyDocument doc,PolicyRevision row) {
        var next=doc.Revisions.Where(r=>r.Topic==row.Topic&&r.Status is "approved" or "withdrawn"&&r.EffectiveFrom>row.EffectiveFrom).Select(r=>(DateTimeOffset?)r.EffectiveFrom).Min();
        return row.EffectiveUntil is null?next:next is null?row.EffectiveUntil:row.EffectiveUntil<next?row.EffectiveUntil:next;
    }}
