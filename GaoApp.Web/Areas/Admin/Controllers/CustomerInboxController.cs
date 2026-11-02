using System.Text.Json;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed record CustomerInboxPage(CustomerStaff Staff,string StoreName,CustomerStoreSettings Settings);
[Area("Admin"),Authorize(Policy=CustomerServiceRights.View),AutoValidateAntiforgeryToken,Route("admin/customer-service"),RequestSizeLimit(24576)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed partial class CustomerInboxController(CustomerServiceAccess access,CustomerServiceStore store,ResourceAdmission admission):Controller
{
    private IActionResult Error(AssistantException e)=>StatusCode(e.Status,e.Body(Guid.Empty));
    private ResourceLease Lease(CustomerStaff who,bool write)=>admission.Enter(write?ResourceGroup.AssistantWork:ResourceGroup.AssistantRead,ResourceSubject.User(who.UserId),who.StoreId);
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct) {
        try{var who=await access.Staff(CustomerServiceRights.View,ct);var current=await access.Store(ct);return View("~/Areas/Admin/Views/CustomerInbox/Index.cshtml",new CustomerInboxPage(who,current.Name,store.Settings(who.StoreId)));}
        catch(AssistantException e){return Error(e);}
    }
    [HttpGet("conversations")]
    public async Task<IActionResult> List(string mode="waiting",int page=0,CancellationToken ct=default,string source="all") {
        try{var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var rows=store.Inbox(who,mode,51,page,source);await access.Recheck(who,CustomerServiceRights.View,ct);
            return Json(new{storeId=who.StoreId,page,hasMore=rows.Length>50,items=rows.Take(50).Select(c=>new{c.Id,c.Name,c.Mode,c.Source,c.ConversationKind,c.Owner,c.OwnerName,c.Version,c.Unread,c.HandoffReason,c.UpdatedAt,c.ExpiresAt})});}
        catch(AssistantException e){return Error(e);}
    }
    [HttpGet("conversations/{id:guid}")]
    public async Task<IActionResult> Get(Guid id,CancellationToken ct) {
        try{var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var row=store.StaffGet(who,id);await access.Recheck(who,CustomerServiceRights.View,ct);return Json(row);}
        catch(AssistantException e){return Error(e);}
    }
    [HttpGet("conversations/{id:guid}/messages/{message:guid}/media/{asset:guid}")]
    public async Task<IActionResult> Media(Guid id,Guid message,Guid asset,bool download,
        [FromServices] CustomerChannelRegistry registry,[FromServices] CustomerChannelTransport transport,CancellationToken ct) {
        try {
            var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,false);
            if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var target=store.StaffZaloMedia(who,id,message,asset);var connection=registry.Get(target.Connection);
            if(connection.StoreId!=who.StoreId||connection.Binding!=target.Binding)throw new AssistantException(404,"customer_media_not_found","Không tìm thấy tệp của hội thoại.");
            if(!connection.Enabled||store.ChannelState(connection).Paused||!store.Settings(who.StoreId).Enabled)
                throw new AssistantException(503,"customer_media_paused","Kênh Zalo đang tạm dừng.");
            await access.Recheck(who,CustomerServiceRights.View,ct);
            var content=await transport.ReadMedia(connection,target,download,ct);
            await access.Recheck(who,CustomerServiceRights.View,ct);
            store.StaffZaloMedia(who,id,message,asset); // Recheck expiry and ownership after downloading.
            var current=registry.Get(target.Connection);
            if(!current.Enabled||current.Binding!=target.Binding||store.ChannelState(current).Paused||!store.Settings(who.StoreId).Enabled)
                throw new AssistantException(503,"customer_media_paused","Kênh Zalo vừa thay đổi hoặc tạm dừng.");
            Response.Headers["X-Content-Type-Options"]="nosniff";Response.Headers["Content-Security-Policy"]="default-src 'none'; sandbox";
            return content.Preview?File(content.Bytes,content.ContentType,enableRangeProcessing:true):
                File(content.Bytes,content.ContentType,content.FileName,enableRangeProcessing:true);
        } catch(AssistantException e){return Error(e);}
        catch(Exception e)when(e is HttpRequestException or OperationCanceledException or IOException or JsonException) {
            return Error(new AssistantException(502,"customer_media_unavailable","Chưa tải được tệp từ ZaloMarketing. Vui lòng thử lại."));
        }
    }
    [HttpPost("conversations/{id:guid}/commands")]
    public async Task<IActionResult> Command(Guid id,[FromBody]JsonElement body,CancellationToken ct) {
        try{var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            if(!ModelState.IsValid||body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);var input=body.Deserialize<CustomerCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();
            var right=input.Action is "claim" or "reply"?CustomerServiceRights.Reply:who.CanManage?CustomerServiceRights.Manage:CustomerServiceRights.Reply;await access.Recheck(who,right,ct);return Json(store.StaffCommand(who,id,input));}
        catch(AssistantException e){return Error(e);}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    [HttpGet("conversations/{id:guid}/feedback")]
    public async Task<IActionResult> Feedback(Guid id,CancellationToken ct){
        try{var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,false);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var items=store.StaffVotes(who,id);await access.Recheck(who,CustomerServiceRights.View,ct);return Json(new{conversationId=id,items});}catch(AssistantException e){return Error(e);}
    }
    [HttpPost("conversations/{id:guid}/feedback")]
    public async Task<IActionResult> Rate(Guid id,[FromBody]JsonElement body,CancellationToken ct){
        try{var who=await access.Staff(CustomerServiceRights.Reply,ct);using var lease=Lease(who,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            if(!ModelState.IsValid||body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();AssistantJwt.Unique(body);var input=body.Deserialize<CustomerRating>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();
            await access.Recheck(who,CustomerServiceRights.Reply,ct);return Json(new{conversationId=id,items=store.RateStaff(who,id,input)});}catch(AssistantException e){return Error(e);}catch(JsonException){return BadRequest(AssistantException.Invalid().Body(Guid.Empty));}
    }
    [HttpPost("settings")]
    [Authorize(Policy=CustomerServiceRights.Manage)]
    public async Task<IActionResult> Settings(bool enabled,long version,CancellationToken ct) {
        try{var who=await access.Staff(CustomerServiceRights.Manage,ct);using var lease=Lease(who,true);if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            await access.Recheck(who,CustomerServiceRights.Manage,ct);store.Configure(who.StoreId,enabled,version);TempData["CustomerServiceResult"]=enabled?"Đã bật chat khách tại /support của cửa hàng này.":"Đã tắt tiếp nhận từ trang chat khách; hộp thư nội bộ vẫn xem được.";return RedirectToAction(nameof(Index));}
        catch(AssistantException e){return Error(e);}
    }
}