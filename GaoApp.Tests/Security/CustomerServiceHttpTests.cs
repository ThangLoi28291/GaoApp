using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Xunit;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerServiceHttpTests
{
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static HttpClient Guest(FullApplicationFixture app,string host){var http=new HttpClient(new HttpClientHandler{CookieContainer=new(),AllowAutoRedirect=false}){BaseAddress=app.Address,Timeout=TimeSpan.FromSeconds(30)};http.DefaultRequestHeaders.Host=host;return http;}
    [Fact]
    public async Task Real_public_cookie_csrf_isolation_inbox_rights_and_midflight_takeover_work_end_to_end() {
        await using var app=await FullApplicationFixture.StartAsync();
        var rights=new[]{CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage};
        var account=await app.AddAccountAsync(app.Stores[0],rights);var otherAccount=await app.AddAccountAsync(app.Stores[1],rights);var readOnly=await app.AddAccountAsync(app.Stores[0],[CustomerServiceRights.View]);
        using var rsa=RSA.Create(2048);var key=Path.Combine(Path.GetTempPath(),"cskh-test-"+Guid.NewGuid().ToString("N")+".pem");await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());using var upstream=new CustomerAiSource();
        try {
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{["Enabled"]="true",["Issuer"]="https://cskh.test",["KeyId"]="cskh-test",["PrivateKeyFile"]=key,["BaseUrl"]=upstream.Address});
            using var own=await app.LoginAsync(account);using var other=await app.LoginAsync(otherAccount);using var reader=await app.LoginAsync(readOnly);
            var admin=await own.Http.GetStringAsync("/admin/customer-service");Assert.Contains("customerInbox",admin);var version=Field(admin,"version");
            using(var enable=await own.Http.PostAsync("/admin/customer-service/settings",new FormUrlEncodedContent(new Dictionary<string,string>{["enabled"]="true",["version"]=version})))Assert.Equal(HttpStatusCode.Redirect,enable.StatusCode);
            using var guest=Guest(app,account.Store.Host);var html=await guest.GetStringAsync("/support");Assert.Contains("customerSupport",html);Assert.Contains("csStartForm",html);var csrf=Field(html,"__RequestVerificationToken");Assert.NotEmpty(csrf);
            using(var rejected=await guest.PostAsJsonAsync("/support/conversations",new{requestId=Guid.NewGuid(),name="Khách TEST",consent=true}))Assert.Equal(HttpStatusCode.BadRequest,rejected.StatusCode);
            guest.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
            var start=new{requestId=Guid.NewGuid(),name="Khách TEST <script>ignored()</script>",contact="TEST contact only",consent=true};
            using var started=await guest.PostAsJsonAsync("/support/conversations",start);Assert.Equal(HttpStatusCode.OK,started.StatusCode);var c=await started.Content.ReadFromJsonAsync<JsonElement>();var id=c.GetProperty("id").GetGuid();
            using(var replay=await guest.PostAsJsonAsync("/support/conversations",start))Assert.Equal(id,(await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
            using var second=Guest(app,account.Store.Host);await second.GetStringAsync("/support");using(var hidden=await second.GetAsync("/support/conversations/"+id))Assert.Equal(HttpStatusCode.NotFound,hidden.StatusCode);
            using(var noCookie=await other.Http.GetAsync("/admin/customer-service/conversations/"+id))Assert.Equal(HttpStatusCode.NotFound,noCookie.StatusCode);
            var ownVersion=c.GetProperty("version").GetInt64();using(var denied=await reader.Http.PostAsJsonAsync("/admin/customer-service/conversations/"+id+"/commands",new{requestId=Guid.NewGuid(),action="claim",version=ownVersion}))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            upstream.BeforeReply=async (incoming,delegation)=>{
                using var lookup=new HttpRequestMessage(HttpMethod.Post,"/api/v1/customer-assistant/context/authorize"){Content=JsonContent.Create(new{requiredPermissions=new[]{"assistant.use","assistant.chat"}})};lookup.Headers.Authorization=new("Bearer",delegation);lookup.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());
                using var allowed=await own.Http.SendAsync(lookup);Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);Assert.True((await allowed.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("allowed").GetBoolean());
                using var variantRequest=new HttpRequestMessage(HttpMethod.Get,"/api/v1/customer-assistant/catalog/variants/"+account.Store.VariantId);variantRequest.Headers.Authorization=new("Bearer",delegation);variantRequest.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());using var variantReply=await own.Http.SendAsync(variantRequest);Assert.Equal(HttpStatusCode.OK,variantReply.StatusCode);
                var retail=await variantReply.Content.ReadFromJsonAsync<JsonElement>();var variant=retail.GetProperty("variant");Assert.Equal(account.Store.VariantId.ToString(),variant.GetProperty("product").GetProperty("variantId").GetString());Assert.DoesNotContain("wholesale",retail.GetRawText());Assert.DoesNotContain("costPrice",retail.GetRawText());
                using var foreignRequest=new HttpRequestMessage(HttpMethod.Get,"/api/v1/customer-assistant/catalog/variants/"+otherAccount.Store.VariantId);foreignRequest.Headers.Authorization=new("Bearer",delegation);foreignRequest.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());using var foreignReply=await own.Http.SendAsync(foreignRequest);Assert.Equal(HttpStatusCode.NotFound,foreignReply.StatusCode);                using var wholesale=new HttpRequestMessage(HttpMethod.Post,"/api/v1/customer-assistant/quotes/lookup"){Content=JsonContent.Create(new{items=Array.Empty<object>(),tiers=new[]{"wholesale"}})};wholesale.Headers.Authorization=new("Bearer",delegation);wholesale.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());using var denied=await own.Http.SendAsync(wholesale);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
                using var employee=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assistant/context/authorize"){Content=JsonContent.Create(new{requiredPermissions=new[]{"assistant.use"}})};employee.Headers.Authorization=new("Bearer",delegation);employee.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());using var separate=await own.Http.SendAsync(employee);Assert.Equal(HttpStatusCode.Unauthorized,separate.StatusCode);
            };
            var payload=new{requestId=Guid.NewGuid(),message="Xin chào cửa hàng"};using(var answer=await guest.PostAsJsonAsync("/support/conversations/"+id+"/messages",payload)){Assert.Equal(HttpStatusCode.OK,answer.StatusCode);c=await answer.Content.ReadFromJsonAsync<JsonElement>();Assert.Contains("TEST AI reply",c.GetRawText());Assert.DoesNotContain("aiState",c.GetRawText());Assert.DoesNotContain("delegation",c.GetRawText());}
            using(var retry=await guest.PostAsJsonAsync("/support/conversations/"+id+"/messages",payload))Assert.Equal(HttpStatusCode.OK,retry.StatusCode);Assert.Equal(1,upstream.Calls);
            upstream.BeforeReply=null;upstream.Block=true;
            var pending=guest.PostAsJsonAsync("/support/conversations/"+id+"/messages",new{requestId=Guid.NewGuid(),message="Tìm sữa tươi TH"});await upstream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using(var handoff=await guest.PostAsJsonAsync("/support/conversations/"+id+"/handoff",new{requestId=Guid.NewGuid(),message="Gặp nhân viên"})){c=await handoff.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("waiting",c.GetProperty("mode").GetString());}
            var command=new{requestId=Guid.NewGuid(),action="claim",version=c.GetProperty("version").GetInt64()};using(var claim=await own.Http.PostAsJsonAsync("/admin/customer-service/conversations/"+id+"/commands",command)){Assert.Equal(HttpStatusCode.OK,claim.StatusCode);c=await claim.Content.ReadFromJsonAsync<JsonElement>();}
            upstream.Release.TrySetResult();using(var late=await pending){var raw=await late.Content.ReadAsStringAsync();Assert.DoesNotContain("LATE_TEST_AI",raw);}
            var reply=new{requestId=Guid.NewGuid(),action="reply",version=c.GetProperty("version").GetInt64(),message="TEST nhân viên: đã tiếp nhận."};using(var sent=await own.Http.PostAsJsonAsync("/admin/customer-service/conversations/"+id+"/commands",reply))Assert.Equal(HttpStatusCode.OK,sent.StatusCode);
            var customer=await guest.GetFromJsonAsync<JsonElement>("/support/conversations/"+id);Assert.Contains("TEST nhân viên",customer.GetProperty("messages").EnumerateArray().Last().GetProperty("text").GetString());
            var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","customer-service-step-c");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"support-page.html"),html);await File.WriteAllTextAsync(Path.Combine(output,"inbox-page.html"),await own.Http.GetStringAsync("/admin/customer-service"));
                        // A restarted worker retains both customer ticket and transcript in isolated storage.
            await app.RestartAsync(enableDeliveryOutbox:false);var resumed=await guest.GetFromJsonAsync<JsonElement>("/support/conversations/"+id);Assert.Equal(customer.GetProperty("messages").GetArrayLength(),resumed.GetProperty("messages").GetArrayLength());
            using(var end=await guest.PostAsJsonAsync("/support/end-session",new{}))Assert.Equal(HttpStatusCode.OK,end.StatusCode);
            using(var ended=await guest.GetAsync("/support/conversations/"+id))Assert.Equal(HttpStatusCode.Unauthorized,ended.StatusCode);        }finally{upstream.Release.TrySetResult();File.Delete(key);}
    }
    private sealed class CustomerAiSource:IDisposable
    {
        private readonly HttpListener listener=new();private readonly CancellationTokenSource stop=new();private readonly Task loop;
        public string Address{get;}public int Calls;public bool Block;public Func<JsonElement,string,Task>? BeforeReply;
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CustomerAiSource(){var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();var port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();Address=$"http://127.0.0.1:{port}/";listener.Prefixes.Add(Address);listener.Start();loop=Task.Run(async()=>{while(!stop.IsCancellationRequested){HttpListenerContext c;try{c=await listener.GetContextAsync().WaitAsync(stop.Token);}catch{break;}try{await Handle(c);}catch(Exception e){c.Response.StatusCode=500;var bytes=System.Text.Encoding.UTF8.GetBytes(e.Message);await c.Response.OutputStream.WriteAsync(bytes);}finally{c.Response.Close();}}});}
        private async Task Handle(HttpListenerContext http){Interlocked.Increment(ref Calls);using var input=await JsonDocument.ParseAsync(http.Request.InputStream);var body=input.RootElement.Clone();var token=http.Request.Headers["X-GaoApp-Delegation"]!;if(BeforeReply is not null)await BeforeReply(body,token);var wasBlocked=Block;if(wasBlocked){Entered.TrySetResult();await Release.Task.WaitAsync(stop.Token);}var visitor=JsonDocument.Parse(Decode(http.Request.Headers["Authorization"]![7..].Split('.')[1]));var store=visitor.RootElement.GetProperty("store_id").GetString();var result=new{contractVersion="1.0",requestId=body.GetProperty("requestId").GetGuid(),conversationId=body.GetProperty("conversationId").GetGuid(),scope=new{storeId=store,warehouseId=(string?)null,legalEntityId=(string?)null},reply=wasBlocked?"LATE_TEST_AI":"TEST AI reply",state=new{conversationId=body.GetProperty("conversationId").GetGuid(),recentMessages=Array.Empty<object>()}};var bytes=JsonSerializer.SerializeToUtf8Bytes(result);http.Response.ContentType="application/json";await http.Response.OutputStream.WriteAsync(bytes);}
        private static byte[] Decode(string s)=>Convert.FromBase64String(s.Replace('-','+').Replace('_','/')+new string('=',(4-s.Length%4)%4));
        public void Dispose(){Release.TrySetResult();stop.Cancel();listener.Close();try{loop.GetAwaiter().GetResult();}catch{}stop.Dispose();}
    }
}