using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using GaoApp.Web.Services.Assistant;

namespace GaoApp.Web.Services.CustomerService;

// First-party, host-only, protected session. Contact/name are self-declared and never authenticate a Customer record.
public sealed class CustomerSession(IDataProtectionProvider provider,TimeProvider clock,IOptionsMonitor<CustomerServiceOptions> options,IWebHostEnvironment environment,CustomerServiceStore storage)
{
    private readonly IDataProtector protector=provider.CreateProtector("GaoApp.CustomerService.Visitor.v1");
    private const string Cookie="GaoApp.Cskh.Visitor";
    private CustomerTicket? Ticket(HttpContext http,int store) {
        if(!http.Request.IsHttps&&!environment.IsDevelopment())throw new AssistantException(400,"customer_https_required","Vui lòng mở chat qua HTTPS.");
        CustomerTicket? ticket=null;
        if(http.Request.Cookies.TryGetValue(Cookie,out var raw)&&raw.Length<4096) {
            try{ticket=JsonSerializer.Deserialize<CustomerTicket>(protector.Unprotect(raw));}catch(Exception e)when(e is System.Security.Cryptography.CryptographicException or JsonException or FormatException){ }
        }
        return ticket is { } t&&t.Version is 1 or 2&&t.StoreId==store&&t.VisitorId!=Guid.Empty&&t.ExpiresAt>clock.GetUtcNow()&&t.ExpiresAt<=clock.GetUtcNow().AddDays(7)?t:null;
    }
    public CustomerTicket Read(HttpContext http,int store,bool create=false) {
        var ticket=Ticket(http,store);
        if(ticket is {} current&&storage.AcceptVisitorSession(current))return current;
        if(!create)throw new AssistantException(401,"customer_session_expired","Phiên hỗ trợ đã hết hạn. Mình mở lại trang để bắt đầu nhé.");
        ticket=new(Guid.NewGuid(),store,clock.GetUtcNow().AddDays(options.CurrentValue.RetentionDays),2);
        storage.RegisterVisitorSession(ticket);
        http.Response.Cookies.Append(Cookie,protector.Protect(JsonSerializer.Serialize(ticket)),new CookieOptions{HttpOnly=true,Secure=http.Request.IsHttps,SameSite=SameSiteMode.Lax,Path="/",Expires=ticket.ExpiresAt,IsEssential=true});return ticket;
    }
    public void End(HttpContext http,int store){var ticket=Ticket(http,store)??throw new AssistantException(401,"customer_session_expired","Phiên hỗ trợ đã hết hạn.");storage.Revoke(ticket);http.Response.Cookies.Delete(Cookie,new CookieOptions{Path="/",HttpOnly=true,Secure=http.Request.IsHttps,SameSite=SameSiteMode.Lax});}}
