using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GaoApp.Web.Controllers;

[AllowAnonymous,IgnoreAntiforgeryToken,Route("integrations/customer-channels/facebook/{connection}"),RequestSizeLimit(262144)]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CustomerChannelWebhookController(CustomerChannelRegistry channels,CustomerServiceStore storage,CustomerServiceAccess access,IWebHostEnvironment environment,TimeProvider clock):Controller
{
    private async Task<CustomerChannelConnection> Current(string id,CancellationToken ct) {
        if(!Request.IsHttps&&!environment.IsDevelopment())throw new AssistantException(400,"customer_channel_https_required","Cần kết nối HTTPS.");
        var c=channels.Get(id);var shop=await access.Store(ct);if(!c.Enabled||c.Provider!="facebook"||c.StoreId!=shop.Id)throw new AssistantException(404,"customer_channel_not_found","Không tìm thấy kênh đang hoạt động.");return c;
    }
    [HttpGet]public async Task<IActionResult> Verify(string connection,[FromQuery(Name="hub.mode")]string? mode,[FromQuery(Name="hub.verify_token")]string? token,[FromQuery(Name="hub.challenge")]string? challenge,CancellationToken ct) {
        try{var c=await Current(connection,ct);var expected=channels.Secret(c.VerifyTokenFile);if(mode!="subscribe"||token is null||token.Length>8192||challenge is null||challenge.Length>1000||!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(token)),SHA256.HashData(Encoding.UTF8.GetBytes(expected))))return StatusCode(403);return Content(challenge,"text/plain");}
        catch(AssistantException e){return StatusCode(e.Status,new{code=e.Code,message=e.Message});}
    }
    [HttpPost]public async Task<IActionResult> Receive(string connection,CancellationToken ct) {
        try{
            var c=await Current(connection,ct);using var body=new MemoryStream();await Request.Body.CopyToAsync(body,ct);if(body.Length>262144)return StatusCode(413);
            var bytes=body.ToArray();if(!CustomerChannelTransport.VerifyFacebook(channels.Secret(c.AppSecretFile),bytes,Request.Headers["X-Hub-Signature-256"].ToString()))return StatusCode(401);
            using var parsed=JsonDocument.Parse(bytes,new JsonDocumentOptions{MaxDepth=32});var items=CustomerChannelTransport.FacebookMessages(c,parsed.RootElement,clock.GetUtcNow());
            // Acknowledge only after durable ingress. Processing and sending run in the background.
            if(storage.ChannelState(c).Paused||!storage.Settings(c.StoreId).Enabled)return StatusCode(503,new{code="customer_channel_paused"});
            foreach(var item in items)storage.ImportChannel(c,item);if(storage.ChannelState(c).Paused||!storage.Settings(c.StoreId).Enabled)return StatusCode(503,new{code="customer_channel_paused"});return Ok(new{accepted=true});
        }catch(AssistantException e){return StatusCode(e.Status,new{code=e.Code,message=e.Message});}
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentOutOfRangeException){return BadRequest(new{code="customer_channel_event_invalid",message="Sự kiện kênh không hợp lệ."});}
    }
}