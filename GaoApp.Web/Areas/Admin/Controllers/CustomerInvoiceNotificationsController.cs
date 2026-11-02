using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GaoApp.Web.Areas.Admin.Controllers;

[Area("Admin"),Authorize(Policy=CustomerServiceRights.View),Authorize(Policy=PermissionCodes.CustomerService.InvoiceNotifications)]
[AutoValidateAntiforgeryToken,Route("admin/customer-service/invoice-notifications"),RequestSizeLimit(16384)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerInvoiceNotificationsController(CustomerServiceAccess access,ICustomerInvoiceNotificationAdmin service,
    ResourceAdmission admission,IOptionsMonitor<InvoiceNotificationOptions> options) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        try
        {
            var who=await access.InvoiceNotificationVerifier(ct);var store=await access.Store(ct);
            return View("~/Areas/Admin/Views/CustomerInvoiceNotifications/Index.cshtml",new CustomerInvoiceNotificationAdminPage(store.Name,who,options.CurrentValue.Enabled));
        }
        catch(AssistantException error){return Error(error);}
    }
    private async Task<IActionResult> Run(bool write,bool reply,Func<CustomerStaff,Task<object?>> action,CancellationToken ct,bool accepted=false)
    {
        try
        {
            var who=await access.InvoiceNotificationVerifier(ct);if(reply&&!who.CanReply)throw AssistantException.Denied();
            using var lease=admission.Enter(write?ResourceGroup.AssistantWork:ResourceGroup.AssistantRead,ResourceSubject.User(who.UserId),who.StoreId);
            if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var result=await action(who);
            var fresh=await access.InvoiceNotificationVerifier(ct);if(fresh.StoreId!=who.StoreId||fresh.UserId!=who.UserId||reply&&!fresh.CanReply)throw AssistantException.Denied();
            return StatusCode(accepted?202:200,result);
        }
        catch(AssistantException error){return Error(error);}
        catch(JsonException){return Error(AssistantException.Invalid());}
        catch(ArgumentException){return Error(AssistantException.Invalid());}
        catch(KeyNotFoundException){return NotFound(new{code="invoice_notification_not_found",message="Không tìm thấy dữ liệu trong cửa hàng hiện tại."});}
        catch(InvalidOperationException){return Conflict(new{code="invoice_notification_changed",message="Dữ liệu, liên kết hoặc mã yêu cầu đã thay đổi. Tải lại và kiểm tra trước khi gửi."});}
    }
    private IActionResult Error(AssistantException error)=>StatusCode(error.Status,error.Body(Guid.Empty));
    private T Input<T>(JsonElement body)
    {
        if(!ModelState.IsValid||body.ValueKind!=JsonValueKind.Object)throw AssistantException.Invalid();
        AssistantJwt.Unique(body);return body.Deserialize<T>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??throw AssistantException.Invalid();
    }
    [HttpGet("customers")]
    public Task<IActionResult> Customers(string query="",CancellationToken ct=default)=>Run(false,false,async who=>new{items=await service.CustomersAsync(who,query,ct)},ct);
    [HttpGet("connections")]
    public Task<IActionResult> Connections(CancellationToken ct)=>Run(false,false,async who=>new{items=await service.ConnectionsAsync(who,ct)},ct);
    [HttpGet("conversations")]
    public Task<IActionResult> Conversations(string connection,string query="",CancellationToken ct=default)=>Run(false,false,async who=>new{items=await service.ConversationsAsync(who,connection,query,ct)},ct);
    [HttpGet("conversations/{conversation:guid}/evidence")]
    public Task<IActionResult> Evidence(Guid conversation,string connection,long sequence,CancellationToken ct)=>Run(false,false,async who=>await service.InspectAsync(who,connection,conversation,sequence,ct),ct);
    [HttpGet("customers/{customer:int}/bindings")]
    public Task<IActionResult> Bindings(int customer,CancellationToken ct)=>Run(false,false,async who=>new{items=await service.BindingsAsync(who,customer,ct)},ct);
    [HttpGet("customers/{customer:int}/sources")]
    public Task<IActionResult> Sources(int customer,CancellationToken ct)=>Run(false,false,async who=>new{items=await service.SourcesAsync(who,customer,ct)},ct);
    [HttpGet("customers/{customer:int}/sources/lookup")]
    public Task<IActionResult> LookupSources(int customer,string query,CancellationToken ct)=>Run(false,false,async who=>new{items=await service.LookupSourcesAsync(who,customer,query,ct)},ct);
    [HttpGet("customers/{customer:int}/source-preview")]
    public Task<IActionResult> Preview(int customer,string sourceType,int sourceId,string kind,CancellationToken ct)=>Run(false,false,async who=>await service.PreviewAsync(who,customer,sourceType,sourceId,kind,ct),ct);
    [HttpPost("bindings")]
    public Task<IActionResult> Verify([FromBody]JsonElement body,CancellationToken ct)=>Run(true,false,async who=>await service.VerifyAsync(who,Input<CustomerInvoiceBindingCommand>(body),ct),ct);
    [HttpPost("bindings/{binding:guid}/revoke")]
    public Task<IActionResult> Revoke(Guid binding,[FromBody]JsonElement body,CancellationToken ct)=>Run(true,false,async who=>{await service.RevokeAsync(who,binding,Input<CustomerInvoiceRevokeCommand>(body),ct);return new{saved=true};},ct);
    [HttpGet("outbox")]
    public Task<IActionResult> Outbox(string? state=null,int page=0,CancellationToken ct=default)=>Run(false,false,async who=>new{items=await service.OutboxAsync(who,state,page,ct),page},ct);
    [HttpGet("outbox/{id:guid}")]
    public Task<IActionResult> Detail(Guid id,CancellationToken ct)=>Run(false,false,async who=>await service.DetailAsync(who,id,ct)??throw new KeyNotFoundException(),ct);
    [HttpPost("outbox"),Authorize(Policy=CustomerServiceRights.Reply)]
    public Task<IActionResult> Enqueue([FromBody]JsonElement body,CancellationToken ct)=>Run(true,true,async who=>new{id=await service.EnqueueAsync(who,Input<CustomerInvoiceSendCommand>(body),ct)},ct,accepted:true);
}
