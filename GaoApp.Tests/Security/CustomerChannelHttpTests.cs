using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Services.CustomerService;
using Xunit;
namespace GaoApp.Tests.Security;
[Collection("SqlServerConcurrency")]
public sealed class CustomerChannelHttpTests
{
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static async Task<JsonElement> Post(HttpClient h,string path,object body){using var response=await h.PostAsJsonAsync(path,body);var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,$"{path}: {(int)response.StatusCode} {text}");return JsonDocument.Parse(text).RootElement.Clone();}
    [Fact]public async Task Zalo_sync_retry_requires_manager_correct_store_csrf_and_current_version_without_any_external_call() {
        var files=Path.Combine(Path.GetTempPath(),"GaoZaloSyncHttpTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(files);
        try {
            File.WriteAllText(Path.Combine(files,"token"),"SYNTHETIC_ZALO_TOKEN_ONLY");
            await using var app=await FullApplicationFixture.StartAsync();
            var account=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View,CustomerServiceRights.Manage);
            var reader=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View);var foreign=await app.AddAccountAsync(app.Stores[1],CustomerServiceRights.View,CustomerServiceRights.Manage);
            await app.ConfigureCustomerChannelsAsync(new Dictionary<string,string>{["Connections__0__Id"]="zalo-sync-test",["Connections__0__StoreId"]=account.Store.StoreId.ToString(),["Connections__0__Provider"]="zalomarketing",["Connections__0__AccountId"]=Guid.NewGuid().ToString("D"),["Connections__0__Enabled"]="true",["Connections__0__AutoReply"]="false",["Connections__0__AccessTokenFile"]=Path.Combine(files,"token"),["Connections__0__BaseUrl"]="http://127.0.0.1:54321/"});
            using var staff=await app.LoginAsync(account);using var read=await app.LoginAsync(reader);using var wrong=await app.LoginAsync(foreign);
            const string route="/admin/customer-service/channels/zalo-sync-test/sync/retry";
            var html=WebUtility.HtmlDecode(await staff.Http.GetStringAsync("/admin/customer-service/channels"));Assert.Contains("Đồng bộ chưa đầy đủ",html);Assert.DoesNotContain("SYNTHETIC_ZALO_TOKEN",html);Assert.DoesNotContain(files,html);
            using(var denied=await read.Http.PostAsJsonAsync(route,new{version=0}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using(var denied=await wrong.Http.PostAsJsonAsync(route,new{version=0}))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            using(var request=new HttpRequestMessage(HttpMethod.Post,route){Content=JsonContent.Create(new{version=0})}){request.Headers.Add("RequestVerificationToken","INVALID_CSRF");using var denied=await staff.Http.SendAsync(request);Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);}
            await Post(staff.Http,route,new{version=0});
            using(var stale=await staff.Http.PostAsJsonAsync(route,new{version=0}))Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
            using(var invalid=await staff.Http.PostAsJsonAsync(route,new{version=1,clearWarning=true}))Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
            html=WebUtility.HtmlDecode(await staff.Http.GetStringAsync("/admin/customer-service/channels"));Assert.Contains("Đồng bộ chưa đầy đủ",html);
        }finally{if(Directory.Exists(files))Directory.Delete(files,true);}
    }
    [Fact]public async Task Facebook_raw_signature_durable_inbox_staff_reply_and_channel_management_work_end_to_end()
    {
        var files=Path.Combine(Path.GetTempPath(),"GaoFacebookChannelTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(files);
        try{
            const string secret="SYNTHETIC_FACEBOOK_APP_SECRET",verify="synthetic-webhook-verify-token";File.WriteAllText(Path.Combine(files,"app-secret"),secret);File.WriteAllText(Path.Combine(files,"verify-token"),verify);File.WriteAllText(Path.Combine(files,"page-token"),"SYNTHETIC_PAGE_TOKEN_ONLY");
            await using var app=await FullApplicationFixture.StartAsync();var account=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage);var reader=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View);var foreign=await app.AddAccountAsync(app.Stores[1],CustomerServiceRights.View,CustomerServiceRights.Manage);
            await app.ConfigureCustomerChannelsAsync(new Dictionary<string,string>{["Connections__0__Id"]="facebook-test",["Connections__0__StoreId"]=account.Store.StoreId.ToString(),["Connections__0__Provider"]="facebook",["Connections__0__AccountId"]="123456",["Connections__0__Enabled"]="true",["Connections__0__AutoReply"]="false",["Connections__0__GraphVersion"]="v26.0",["Connections__0__AccessTokenFile"]=Path.Combine(files,"page-token"),["Connections__0__AppSecretFile"]=Path.Combine(files,"app-secret"),["Connections__0__VerifyTokenFile"]=Path.Combine(files,"verify-token")});
            using var staff=await app.LoginAsync(account);using var read=await app.LoginAsync(reader);using var wrong=await app.LoginAsync(foreign);
            var inbox=await staff.Http.GetStringAsync("/admin/customer-service");using(var enabled=await staff.Http.PostAsync("/admin/customer-service/settings",new FormUrlEncodedContent(new Dictionary<string,string>{["enabled"]="true",["version"]=Field(inbox,"version")})))Assert.Equal(HttpStatusCode.Redirect,enabled.StatusCode);
            using var guest=app.Anonymous(account.Store);var hook="/integrations/customer-channels/facebook/facebook-test";
            using(var handshake=await guest.Http.GetAsync(hook+"?hub.mode=subscribe&hub.verify_token="+verify+"&hub.challenge=12345")){Assert.Equal(HttpStatusCode.OK,handshake.StatusCode);Assert.Equal("12345",await handshake.Content.ReadAsStringAsync());}
            using(var denied=await guest.Http.GetAsync(hook+"?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=12345"))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            var body=JsonSerializer.Serialize(new{@object="page",entry=new[]{new{id="123456",messaging=new[]{new{sender=new{id="999111"},recipient=new{id="123456"},timestamp=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),message=new{mid="TEST-MID-1",text="TEST <img src=x onerror=alert(1)>"}}}}}});
            async Task<HttpResponseMessage> Send(string raw,string? overrideSignature=null){using var request=new HttpRequestMessage(HttpMethod.Post,hook){Content=new StringContent(raw,Encoding.UTF8,"application/json")};request.Headers.Add("X-Hub-Signature-256",overrideSignature??"sha256="+Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),Encoding.UTF8.GetBytes(raw))).ToLowerInvariant());return await guest.Http.SendAsync(request);}
            using(var bad=await Send(body,"sha256="+new string('0',64)))Assert.Equal(HttpStatusCode.Unauthorized,bad.StatusCode);
            using(var received=await Send(body))Assert.Equal(HttpStatusCode.OK,received.StatusCode);using(var replay=await Send(body))Assert.Equal(HttpStatusCode.OK,replay.StatusCode);
            using(var duplicate=await Send("{\"object\":\"page\",\"object\":\"page\",\"entry\":[]}"))Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);
            var list=await staff.Http.GetFromJsonAsync<JsonElement>("/admin/customer-service/conversations?mode=all");var row=Assert.Single(list.GetProperty("items").EnumerateArray());var id=row.GetProperty("id").GetGuid();Assert.Equal("facebook",row.GetProperty("source").GetString());
            var conversation=await staff.Http.GetFromJsonAsync<JsonElement>($"/admin/customer-service/conversations/{id}");Assert.Equal(2,conversation.GetProperty("messages").GetArrayLength());Assert.Contains("<img",conversation.GetProperty("messages").EnumerateArray().Last().GetProperty("text").GetString());
            using(var scoped=await wrong.Http.GetAsync($"/admin/customer-service/conversations/{id}"))Assert.Equal(HttpStatusCode.NotFound,scoped.StatusCode);
            conversation=await Post(staff.Http,$"/admin/customer-service/conversations/{id}/commands",new{requestId=Guid.NewGuid(),action="claim",version=conversation.GetProperty("version").GetInt64()});
            var command=new{requestId=Guid.NewGuid(),action="reply",version=conversation.GetProperty("version").GetInt64(),message="TEST staff answer"};conversation=await Post(staff.Http,$"/admin/customer-service/conversations/{id}/commands",command);await Post(staff.Http,$"/admin/customer-service/conversations/{id}/commands",command);
            Assert.Equal("pending",conversation.GetProperty("messages").EnumerateArray().Last().GetProperty("delivery").GetProperty("state").GetString());
            var channels=await staff.Http.GetStringAsync("/admin/customer-service/channels");Assert.Contains("customerChannels",channels);Assert.DoesNotContain(secret,channels);Assert.DoesNotContain("SYNTHETIC_PAGE_TOKEN",channels);Assert.DoesNotContain(files,channels);
            var deliveries=await staff.Http.GetFromJsonAsync<JsonElement>("/admin/customer-service/channels/facebook-test/deliveries");var item=Assert.Single(deliveries.GetProperty("items").EnumerateArray());Assert.Equal("pending",item.GetProperty("state").GetString());var delivery=item.GetProperty("id").GetString();
            using(var denied=await read.Http.PostAsJsonAsync("/admin/customer-service/channels/facebook-test/pause",new{paused=true,version=0}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            using(var denied=await wrong.Http.GetAsync("/admin/customer-service/channels/facebook-test/deliveries"))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            await Post(staff.Http,$"/admin/customer-service/channels/facebook-test/deliveries/{delivery}/review",new{requestId=Guid.NewGuid(),action="discard",confirmedNotSent=false});
            await Post(staff.Http,"/admin/customer-service/channels/facebook-test/pause",new{paused=true,version=0});using(var paused=await Send(body.Replace("TEST-MID-1","TEST-MID-2")))Assert.Equal(HttpStatusCode.ServiceUnavailable,paused.StatusCode);
            await app.RestartAsync(enableDeliveryOutbox:false);var persisted=await staff.Http.GetFromJsonAsync<JsonElement>("/admin/customer-service/channels/facebook-test/deliveries");Assert.Equal("cancelled",Assert.Single(persisted.GetProperty("items").EnumerateArray()).GetProperty("state").GetString());
            var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","customer-service-step-e");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"channels-page.html"),channels);await File.WriteAllTextAsync(Path.Combine(output,"inbox-page.html"),inbox);
            await app.ConfigureCustomerChannelsAsync(new Dictionary<string,string>{
                ["Enabled"]="false",
                ["CredentialDirectory"]="legacy-channel-credentials",
                ["Connections__0__Enabled"]="true",
                ["Connections__0__AccessTokenFile"]="App_Data/legacy-facebook-token.txt",
                ["Connections__0__AppSecretFile"]="App_Data/legacy-facebook-secret.txt",
                ["Connections__0__VerifyTokenFile"]="App_Data/legacy-facebook-verify.txt"});
            using(var disabledHandshake=await guest.Http.GetAsync(hook+"?hub.mode=subscribe&hub.verify_token="+verify+"&hub.challenge=12345"))Assert.Equal(HttpStatusCode.NotFound,disabledHandshake.StatusCode);
            using(var disabledReceive=await Send(body))Assert.Equal(HttpStatusCode.NotFound,disabledReceive.StatusCode);
            using(var disabledDeliveries=await staff.Http.GetAsync("/admin/customer-service/channels/facebook-test/deliveries"))Assert.Equal(HttpStatusCode.NotFound,disabledDeliveries.StatusCode);
            var disabledChannels=WebUtility.HtmlDecode(await staff.Http.GetStringAsync("/admin/customer-service/channels"));
            Assert.Contains("Facebook/Zalo đang tắt",disabledChannels);
            Assert.DoesNotContain(secret,disabledChannels);Assert.DoesNotContain("SYNTHETIC_PAGE_TOKEN",disabledChannels);Assert.DoesNotContain(files,disabledChannels);Assert.DoesNotContain("legacy-facebook",disabledChannels);Assert.DoesNotContain("facebook-test",disabledChannels);
        }finally{if(Directory.Exists(files))Directory.Delete(files,true);}
    }
}
