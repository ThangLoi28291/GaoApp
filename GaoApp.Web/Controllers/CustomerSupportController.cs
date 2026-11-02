using System.Text.Json;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Controllers;

public sealed record CustomerSupportPage(string StoreName,int StoreId,bool Enabled,int RetentionDays);
[AllowAnonymous,AutoValidateAntiforgeryToken,Route("support"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerSupportController(CustomerServiceAccess access,CustomerSession sessions,CustomerServiceStore store,CustomerAiBridge ai,
    ResourceAdmission admission,IOptionsMonitor<CustomerServiceOptions> options,IHostApplicationLifetime lifetime):Controller
{
    private static readonly JsonSerializerOptions Wire=new(JsonSerializerDefaults.Web);
    private static T Parse<T>(JsonElement body){if(body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);return body.Deserialize<T>(Wire)??throw AssistantException.Invalid();}
    // Do not expose visitor IDs, agent IDs, source tokens or the opaque AI context in the public transcript.
    private static object Public(CustomerConversation c)=>new{c.Id,c.Name,c.Mode,c.OwnerName,c.Version,c.CreatedAt,c.UpdatedAt,c.ExpiresAt,c.Messages};
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) {
        try{var current=await access.Store(ct);var enabled=store.Settings(current.Id).Enabled;
            if(enabled)sessions.Read(HttpContext,current.Id,true);
            return View("~/Views/CustomerSupport/Index.cshtml",new CustomerSupportPage(current.Name,current.Id,enabled,options.CurrentValue.RetentionDays));
        }catch(AssistantException e){return Error(e);}
    }
    private async Task<(int Store,Guid Visitor,DateTimeOffset Expires)> Who(CancellationToken ct) {var current=await access.Store(ct);var ticket=sessions.Read(HttpContext,current.Id);return(current.Id,ticket.VisitorId,ticket.ExpiresAt);}
    private ResourceLease Lease(int current,Guid visitor,bool write)=>admission.Enter(write?ResourceGroup.CustomerWork:ResourceGroup.CustomerRead,ResourceSubject.Visitor(visitor),current);
    private IActionResult Error(Exception e) {if(e is AssistantException {Status:429})Response.Headers.RetryAfter="60";return e switch {AssistantException a=>StatusCode(a.Status,a.Body(Guid.Empty)),JsonException=>BadRequest(new{code="invalid_request",message="Nội dung gửi chưa hợp lệ."}),_=>StatusCode(503,new{code="customer_service_unavailable",message="Chưa kết nối được cửa hàng. Vui lòng thử lại."})};}
    [HttpGet("session")]
    public async Task<IActionResult> Session(CancellationToken ct) {
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            return Json(new{storeId=who.Store,items=store.Mine(who.Store,who.Visitor).Select(c=>new{c.Id,c.Mode,c.UpdatedAt,c.ExpiresAt})});}catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
    [HttpPost("end-session")]
    public async Task<IActionResult> End(CancellationToken ct){try{var current=await access.Store(ct);sessions.End(HttpContext,current.Id);return Json(new{ended=true});}catch(AssistantException e){return Error(e);}}    [HttpPost("conversations")]
    public async Task<IActionResult> Start([FromBody]JsonElement body,CancellationToken ct) {
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            return Json(Public(store.Start(who.Store,who.Visitor,Parse<CustomerStart>(body),who.Expires)));}catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct) {
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            return Json(Public(store.Get(who.Store,who.Visitor,id)));}catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
    [HttpPost("conversations/{id:guid}/messages")]
    public async Task<IActionResult> Send(Guid id,[FromBody]JsonElement body,CancellationToken ct) {
        try{
            var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var input=Parse<CustomerSend>(body);var privateQuery=CustomerBusinessRead.PrivateQuery(input.Message);var turn=store.Begin(who.Store,who.Visitor,id,input,privateQuery?null:CustomerAiBridge.Handoff(input.Message));
            if(!turn.RunAi)return Json(Public(turn.Conversation));
            var bridge=new CustomerBridgeIdentity(who.Visitor,who.Store,id,input.RequestId);
            if(privateQuery)return Json(Public(store.Complete(bridge,CustomerBusinessRead.PrivateReply,turn.StateJson,null)));
            try{
                // Once accepted, a browser close must not leave an uncommitted reply. Shutdown/timeout queues staff.
                var result=await ai.Reply(bridge,input.Message,turn.StateJson,lifetime.ApplicationStopping);
                return Json(Public(store.Complete(bridge,result.Reply,result.State,result.NeedsHuman?"needs_human":null)));
            }catch(Exception e)when(e is HttpRequestException or OperationCanceledException or JsonException or AssistantException or InvalidOperationException or IOException){
                return Json(Public(store.Complete(bridge,null,null,"ai_unavailable")));
            }
        }catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
    [HttpGet("conversations/{id:guid}/feedback")]
    public async Task<IActionResult> Feedback(Guid id,CancellationToken ct){
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);return Json(new{conversationId=id,items=store.CustomerVotes(who.Store,who.Visitor,id)});}catch(AssistantException e){return Error(e);}
    }
    [HttpPost("conversations/{id:guid}/feedback")]
    public async Task<IActionResult> Rate(Guid id,[FromBody]JsonElement body,CancellationToken ct){
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);return Json(new{conversationId=id,items=store.RateCustomer(who.Store,who.Visitor,id,Parse<CustomerRating>(body))});}catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
    [HttpPost("conversations/{id:guid}/handoff")]
    public async Task<IActionResult> Handoff(Guid id,[FromBody]JsonElement body,CancellationToken ct) {
        try{var who=await Who(ct);using var lease=Lease(who.Store,who.Visitor,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var input=Parse<CustomerSend>(body);return Json(Public(store.Handoff(who.Store,who.Visitor,id,input.RequestId)));}catch(Exception e)when(e is AssistantException or JsonException){return Error(e);}
    }
}
