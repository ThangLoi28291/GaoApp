using System.Text.Json;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record CustomerChannelRow(string Id,string Label,string AccountHint,bool Enabled,bool AutoReply,CustomerChannelState? State,string? Error,string? LivePilotPhone=null);
public sealed record CustomerChannelsPage(string StoreName,bool CanManage,CustomerChannelRow[] Connections,bool WorkerEnabled,bool Enabled=true);
[Area("Admin"),Authorize(Policy=CustomerServiceRights.View),AutoValidateAntiforgeryToken,Route("admin/customer-service/channels"),RequestSizeLimit(4096)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerChannelsController(CustomerServiceAccess access,CustomerChannelRegistry registry,CustomerServiceStore storage,ResourceAdmission admission):Controller
{
    [HttpGet("")]public async Task<IActionResult> Index(CancellationToken ct) {
        try{var who=await access.Staff(CustomerServiceRights.View,ct);var store=await access.Store(ct);var rows=registry.All().Where(c=>c.StoreId==who.StoreId).Select(c=>{CustomerChannelState? state=null;string? error=null;try{state=storage.ChannelState(c);}catch(AssistantException e){error=e.Code;}return new CustomerChannelRow(c.Id,c.Label,"••••"+c.AccountId[^Math.Min(4,c.AccountId.Length)..],c.Enabled,c.AutoReply,state,error,c.LiveReplyPilot.Enabled?c.LiveReplyPilot.Phone:null);}).ToArray();await access.Recheck(who,CustomerServiceRights.View,ct);return View("~/Areas/Admin/Views/CustomerChannels/Index.cshtml",new CustomerChannelsPage(store.Name,who.CanManage,rows,registry.WorkerEnabled,registry.Enabled));}
        catch(AssistantException e){return StatusCode(e.Status,e.Body(Guid.Empty));}
    }
    private async Task<IActionResult> Run(string connection,bool manage,Func<CustomerStaff,CustomerChannelConnection,object> action,CancellationToken ct) {
        try{var right=manage?CustomerServiceRights.Manage:CustomerServiceRights.View;var who=await access.Staff(right,ct);var c=registry.Get(connection);if(c.StoreId!=who.StoreId)throw new AssistantException(404,"customer_channel_not_found","Không tìm thấy kênh tại cửa hàng.");
            using var lease=admission.Enter(manage?ResourceGroup.AssistantWork:ResourceGroup.AssistantRead,ResourceSubject.User(who.UserId),who.StoreId);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            await access.Recheck(who,right,ct);return Json(action(who,c));}
        catch(AssistantException e){return StatusCode(e.Status,e.Body(Guid.Empty));}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    private static T Input<T>(JsonElement body){if(body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);return body.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();}
    [HttpPost("{connection}/pause"),Authorize(Policy=CustomerServiceRights.Manage)]public Task<IActionResult> Pause(string connection,[FromBody]JsonElement body,CancellationToken ct)=>Run(connection,true,(who,c)=>{var input=Input<CustomerChannelControl>(body);storage.PauseChannel(c,input.Paused,input.Version);return new{saved=true};},ct);
    [HttpGet("{connection}/deliveries")]public Task<IActionResult> Deliveries(string connection,CancellationToken ct)=>Run(connection,false,(who,c)=>new{connection,items=storage.ChannelDeliveries(who,c.Id)},ct);
    [HttpPost("{connection}/sync/retry"),Authorize(Policy=CustomerServiceRights.Manage)]public Task<IActionResult> RetrySync(string connection,[FromBody]JsonElement body,CancellationToken ct)=>Run(connection,true,(who,c)=>{storage.RetryZaloSync(who,c,Input<CustomerZaloSyncRetry>(body).Version);return new{saved=true};},ct);
    [HttpPost("{connection}/deliveries/{id:guid}/review"),Authorize(Policy=CustomerServiceRights.Manage)]public Task<IActionResult> Review(string connection,Guid id,[FromBody]JsonElement body,CancellationToken ct)=>Run(connection,true,(who,c)=>{storage.ReviewChannelDispatch(who,c,id,Input<CustomerChannelReview>(body));return new{saved=true};},ct);
}
