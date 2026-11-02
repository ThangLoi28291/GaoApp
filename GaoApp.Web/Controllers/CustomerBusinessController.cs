using System.Text.Json;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[AllowAnonymous,AutoValidateAntiforgeryToken,Route("support/conversations/{id:guid}/private"),RequestSizeLimit(4096)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerBusinessController(CustomerServiceAccess access,CustomerSession sessions,CustomerServiceStore storage,CustomerBusinessRead business,ResourceAdmission admission):Controller
{
    private async Task<IActionResult> Run(Guid id,bool write,Func<int,Guid,Task<object>> action,CancellationToken ct) {
        try{var current=await access.Store(ct);var visitor=sessions.Read(HttpContext,current.Id).VisitorId;storage.Get(current.Id,visitor,id);
            using var lease=admission.Enter(write?ResourceGroup.CustomerWork:ResourceGroup.CustomerRead,ResourceSubject.Visitor(visitor),current.Id);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            Response.Headers.CacheControl="no-store, private";Response.Headers["Referrer-Policy"]="no-referrer";return Json(await action(current.Id,visitor));}
        catch(AssistantException e){return StatusCode(e.Status,e.Body(Guid.Empty));}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    private static T Input<T>(JsonElement body){if(body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);return body.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();}
    [HttpGet("status")]public Task<IActionResult> Status(Guid id,CancellationToken ct)=>Run(id,false,(s,v)=>business.Status(s,v,id,ct),ct);
    [HttpPost("verify")]public Task<IActionResult> Verify(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,true,(s,v)=>business.Redeem(s,v,id,Input<CustomerIdentityRedeem>(body),ct),ct);
    [HttpPost("revoke")]public Task<IActionResult> Revoke(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,true,(s,v)=>{storage.RevokeIdentity(s,v,id,Input<CustomerIdentityRevoke>(body).RequestId);return Task.FromResult<object>(new{verified=false});},ct);
    [HttpGet("orders")]public Task<IActionResult> Orders(Guid id,int page=0,CancellationToken ct=default)=>Run(id,false,(s,v)=>business.OrderList(s,v,id,page,ct),ct);
    [HttpPost("order")]public Task<IActionResult> Order(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,false,(s,v)=>business.OrderDetail(s,v,id,Input<OrderQuery>(body).Number,ct),ct);
    [HttpGet("rewards")]public Task<IActionResult> Rewards(Guid id,CancellationToken ct)=>Run(id,false,(s,v)=>business.Rewards(s,v,id,ct),ct);
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public sealed record OrderQuery(string Number);
}