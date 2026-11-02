using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Services.CustomerService;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerServiceCapacityHttpTests
{
    [Fact]public async Task Full_capacity_keeps_current_visitors_and_logout_usable_and_blocks_cookie_replay_after_restart() {
        await using var app=await FullApplicationFixture.StartAsync();
        var account=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View,CustomerServiceRights.Manage);
        await app.ConfigureCustomerServiceAsync(new Dictionary<string,string>{["MaximumVisitorSessionsPerStore"]="2",["MaximumVisitorSessionsTotal"]="3"});
        using var manager=await app.LoginAsync(account);var html=await manager.Http.GetStringAsync("/admin/customer-service");var version=Regex.Match(html,"name=\"version\"[^>]*value=\"([^\"]*)\"").Groups[1].Value;
        using(var enabled=await manager.Http.PostAsync("/admin/customer-service/settings",new FormUrlEncodedContent(new Dictionary<string,string>{["enabled"]="true",["version"]=version})))Assert.Equal(HttpStatusCode.Redirect,enabled.StatusCode);
        var cookies=new CookieContainer();using var first=new HttpClient(new HttpClientHandler{CookieContainer=cookies,AllowAutoRedirect=false}){BaseAddress=app.Address};first.DefaultRequestHeaders.Host=account.Store.Host;
        html=await first.GetStringAsync("/support");var csrf=WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);Assert.NotEmpty(csrf);first.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
        var cookieHeader=cookies.GetCookieHeader(app.Address);Assert.Contains("GaoApp.Cskh.Visitor",cookieHeader);
        using var second=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=app.Address};second.DefaultRequestHeaders.Host=account.Store.Host;await second.GetStringAsync("/support");
        using var third=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=app.Address};third.DefaultRequestHeaders.Host=account.Store.Host;
        using(var rejected=await third.GetAsync("/support")){Assert.Equal(HttpStatusCode.TooManyRequests,rejected.StatusCode);Assert.Equal("60",rejected.Headers.GetValues("Retry-After").Single());Assert.Equal("customer_session_capacity",(await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());Assert.False(rejected.Headers.TryGetValues("Set-Cookie",out var set)&&set.Any(v=>v.StartsWith("GaoApp.Cskh.Visitor=")));}
        using(var existing=await first.GetAsync("/support/session"))Assert.Equal(HttpStatusCode.OK,existing.StatusCode);
        using(var request=new HttpRequestMessage(HttpMethod.Post,"/support/end-session"){Content=JsonContent.Create(new{})}){request.Headers.Add("RequestVerificationToken","INVALID");using var denied=await first.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
        using(var ended=await first.PostAsJsonAsync("/support/end-session",new{}))Assert.Equal(HttpStatusCode.OK,ended.StatusCode);
        await app.RestartAsync(enableDeliveryOutbox:false);
        using var replay=new HttpClient(new HttpClientHandler{UseCookies=false,AllowAutoRedirect=false}){BaseAddress=app.Address};replay.DefaultRequestHeaders.Host=account.Store.Host;replay.DefaultRequestHeaders.Add("Cookie",cookieHeader);
        using(var denied=await replay.GetAsync("/support/session"))Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        using(var stillFull=await third.GetAsync("/support"))Assert.Equal(HttpStatusCode.TooManyRequests,stillFull.StatusCode);
        using(var active=await second.GetAsync("/support/session"))Assert.Equal(HttpStatusCode.OK,active.StatusCode);
    }
}
