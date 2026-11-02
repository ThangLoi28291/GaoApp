using GaoApp.Web.Security;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Areas.Admin.Controllers;

public sealed partial class CustomerInboxController
{
    [HttpPost("conversations/{id:guid}/media-replies"),Authorize(Policy=CustomerServiceRights.Reply)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(CustomerOutgoingMedia.MaximumFileBytes+65536)]
    [RequestFormLimits(MultipartBodyLengthLimit=CustomerOutgoingMedia.MaximumFileBytes+65536,ValueCountLimit=8,ValueLengthLimit=16384)]
    public async Task<IActionResult> SendMedia(Guid id,[FromForm]CustomerMediaReply input,[FromServices]CustomerChannelRegistry registry,CancellationToken ct)
    {
        try{
            var who=await access.Staff(CustomerServiceRights.Reply,ct);using var lease=Lease(who,true);
            if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            if(!ModelState.IsValid||input.RequestId==Guid.Empty||input.Asset is null||Request.Form.Files.Count!=1||
                !string.Equals(Request.Form.Files[0].Name,"Asset",StringComparison.OrdinalIgnoreCase))throw AssistantException.Invalid();
            var connectionId=store.StaffMediaConnection(who,id);
            var asset=await CustomerOutgoingMedia.Inspect(input.Asset,ct);
            await access.Recheck(who,CustomerServiceRights.Reply,ct);
            return Json(store.StaffMediaReply(who,id,input,asset,registry.Get(connectionId)));
        }catch(AssistantException e){return Error(e);}
    }
    [HttpGet("conversations/{id:guid}/messages/{message:guid}/outgoing-media")]
    public async Task<IActionResult> OutgoingMedia(Guid id,Guid message,bool download,CancellationToken ct)
    {
        try{
            var who=await access.Staff(CustomerServiceRights.View,ct);using var lease=Lease(who,false);
            if(!lease.IsAcquired)return ResourceProtectionResponse.Result(HttpContext,lease);
            var media=store.StaffOutgoingMedia(who,id,message);await access.Recheck(who,CustomerServiceRights.View,ct);
            store.StaffGet(who,id);
            Response.Headers["X-Content-Type-Options"]="nosniff";Response.Headers["Content-Security-Policy"]="default-src 'none'; sandbox";
            return !download&&media.Attachment.Kind=="Image"?File(media.Bytes,media.Attachment.ContentType,enableRangeProcessing:true):
                File(media.Bytes,"application/octet-stream",media.Attachment.FileName,enableRangeProcessing:true);
        }catch(AssistantException e){return Error(e);}
    }
}
