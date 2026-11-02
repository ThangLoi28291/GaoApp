using System.Text.Json;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"),Authorize(Policy=CustomerServiceRights.Verify),AutoValidateAntiforgeryToken,Route("admin/customer-service/conversations/{id:guid}/identity"),RequestSizeLimit(4096)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerVerificationController(CustomerServiceAccess access,CustomerBusinessRead business,CustomerServiceStore storage,ResourceAdmission admission):Controller
{
    private async Task<IActionResult> Run(Guid id,bool write,Func<CustomerStaff,Task<object>> action,CancellationToken ct) {
        try{var who=await access.Verifier(ct);using var lease=admission.Enter(write?ResourceGroup.AssistantWork:ResourceGroup.AssistantRead,ResourceSubject.User(who.UserId),who.StoreId);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            storage.StaffGet(who,id);return Json(await action(who));}
        catch(AssistantException e){return StatusCode(e.Status,e.Body(Guid.Empty));}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    private static T Input<T>(JsonElement body){if(body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);return body.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();}
    [HttpPost("customers")]public Task<IActionResult> Customers(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,false,who=>business.Customers(who,id,Input<CustomerQuery>(body).Query,ct),ct);
    [HttpPost("issue")]public Task<IActionResult> Issue(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,true,async who=>(object)await business.Issue(who,id,Input<CustomerIdentityIssue>(body),ct),ct);
    [HttpPost("revoke")]public Task<IActionResult> Revoke(Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(id,true,async who=>{var input=Input<CustomerIdentityRevoke>(body);var c=storage.StaffGet(who,id);if(c.Owner!=who.UserId&&!who.CanManage)throw AssistantException.Denied();await access.Verifier(ct);storage.RevokeIdentity(who.StoreId,c.VisitorId,id,input.RequestId,who.UserId);return new{revoked=true};},ct);
    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    public sealed record CustomerQuery(string Query);
}