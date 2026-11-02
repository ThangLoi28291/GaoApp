using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Acb;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class ResourceProtectionHttpTests
{
    [Fact]
    public async Task Expensive_reads_and_uploads_have_independent_quotas_preserve_CSRF_and_leave_POS_writes_available()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["Limits__HeavyRead__CallerPerWindow"]="1",["Limits__Upload__CallerPerWindow"]="1" });
        using var own=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using var foreign=await app.LoginAsync(await app.AddAccountAsync(app.Stores[1],"*"));
        using(var allowed=await own.Http.GetAsync("/admin/media-library/data")) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using(var limited=await own.Http.GetAsync("/admin/media-library/data?storeId="+app.Stores[1].StoreId)) Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        using(var allowed=await foreign.Http.GetAsync("/admin/media-library/data")) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        var csrf=own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using(var invalid=await Upload()) Assert.Equal(HttpStatusCode.BadRequest,invalid.StatusCode);
        own.Http.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
        using(var allowed=await Upload()) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
        using(var limited=await Upload()) Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        await own.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=0,warehouseId=store.WarehouseId});
        using(var sale=await own.Http.PostAsJsonAsync("/admin/pos/cart/current/new",new {})) Assert.Equal(HttpStatusCode.OK,sale.StatusCode);
        await using var db=app.Database.CreateTenantContext(store.StoreId);Assert.Single(await db.MediaAssets.ToListAsync());
        async Task<HttpResponseMessage> Upload()
        {
            using var body=new MultipartFormDataContent();var image=new ByteArrayContent(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j0ZkAAAAASUVORK5CYII="));
            image.Headers.ContentType=new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");body.Add(image,"file","resource-test.png");return await own.Http.PostAsync("/admin/media/temp",body);
        }
    }
    [Fact]
    public async Task Early_ingress_rejects_before_body_parse_and_untrusted_forwarded_ips_cannot_reset_it()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["Limits__PhoneIngress__CallerPerWindow"] = "2" });
        using var client = app.Anonymous(app.Stores[0]);
        for (var i=0;i<3;i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,"/api/bank-notifications") { Content = new StringContent("{invalid-json",System.Text.Encoding.UTF8,"application/json") };
            request.Headers.Add("X-Forwarded-For",$"198.51.100.{i+1}");
            using var response = await client.Http.SendAsync(request);
            Assert.Equal(i<2 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests,response.StatusCode);
            if(i==2) { Assert.NotNull(response.Headers.RetryAfter); Assert.Contains("resource_busy", await response.Content.ReadAsStringAsync()); }
        }
    }
    [Fact]
    public async Task Real_AI_requests_enforce_user_concurrency_and_rate_without_blocking_other_users_stores_or_cancel()
    {
        await using var app = await FullApplicationFixture.StartAsync();
        var a = await app.AddAccountAsync(app.Stores[0], AssistantPermissions.Use, AssistantPermissions.Chat);
        var b = await app.AddAccountAsync(app.Stores[0], AssistantPermissions.Use, AssistantPermissions.Chat);
        var c = await app.AddAccountAsync(app.Stores[1], AssistantPermissions.Use, AssistantPermissions.Chat);
        using var first = await app.LoginAsync(a); using var second = await app.LoginAsync(b); using var other = await app.LoginAsync(c);
        using var upstream = new Upstream(); using var rsa = RSA.Create(2048);
        var key = Path.Combine(FullApplicationFixture.SourceRoot(),".artifacts","security-issue11","test-key-"+Guid.NewGuid()+".pem");
        await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());
        try
        {
            await app.ConfigureAssistantAsync(new Dictionary<string,string> { ["Enabled"]="true",["BaseUrl"]=upstream.Address,["Issuer"]="https://gao.test",["KeyId"]="resource-test",["PrivateKeyFile"]=key });
            upstream.Block = true;
            var one = Chat(first); var two = Chat(first);
            await upstream.TwoStarted.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using (var denied = await Chat(first)) { Assert.Equal(HttpStatusCode.TooManyRequests,denied.StatusCode); Assert.NotNull(denied.Headers.RetryAfter); }
            using (var status = await first.Http.PostAsJsonAsync("/admin/api/assistant/call/RequestStatus",new { resourceId=Guid.NewGuid() })) Assert.Equal(HttpStatusCode.OK,status.StatusCode);
            using (var cancel = await first.Http.PostAsJsonAsync("/admin/api/assistant/call/CancelRequest",new { resourceId=Guid.NewGuid() })) Assert.Equal(HttpStatusCode.OK,cancel.StatusCode);
            upstream.Block = false;
            using (var sameNat = await Chat(second)) Assert.Equal(HttpStatusCode.OK,sameNat.StatusCode);
            using (var separateStore = await Chat(other)) Assert.Equal(HttpStatusCode.OK,separateStore.StatusCode);
            upstream.Release.TrySetResult();
            using (var result = await one) Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            using (var result = await two) Assert.Equal(HttpStatusCode.OK,result.StatusCode);
            using (var after = await Chat(first)) Assert.Equal(HttpStatusCode.OK,after.StatusCode);
            // Test low quotas through the actual entry point; identity is always taken from the authenticated account.
            await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["Limits__AssistantWork__CallerPerWindow"]="1",["Limits__AssistantWork__StorePerWindow"]="2" });
            using (var allowed = await Chat(first)) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
            var calls=upstream.Calls;
            using (var denied = await Chat(first)) Assert.Equal(HttpStatusCode.TooManyRequests,denied.StatusCode);
            Assert.Equal(calls,upstream.Calls);
            using (var allowed = await Chat(second)) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
            using (var allowed = await Chat(other)) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
            using (var context = await first.Http.GetAsync("/admin/api/assistant/context?storeId="+c.Store.StoreId)) Assert.Equal(HttpStatusCode.OK,context.StatusCode);
            await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["Limits__AssistantSource__CallerPerWindow"]="3" });
            using var source=app.Anonymous(a.Store);
            var delegation=Delegation(rsa,a);
            async Task<HttpResponseMessage> Source(string path,string json,string credential)
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,path) {Content=new StringContent(json,System.Text.Encoding.UTF8,"application/json")};
                request.Headers.Add("Authorization","Bearer "+credential);request.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());
                return await source.Http.SendAsync(request);
            }
            const string authorize="/api/v1/assistant/context/authorize";
            var authorization=JsonSerializer.Serialize(new {requiredPermissions=new[]{AssistantPermissions.Use}});
            using(var invalid=await Source(authorize,"{bad","invalid")) Assert.Equal(HttpStatusCode.Unauthorized,invalid.StatusCode);
            using(var allowed=await Source(authorize,authorization,delegation)) Assert.Equal(HttpStatusCode.OK,allowed.StatusCode);
            using(var bad=await Source(authorize,"{\"requiredPermissions\":[\"unknown\"]}",delegation))
            { Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode); Assert.Contains("contractVersion",await bad.Content.ReadAsStringAsync()); }
            using(var duplicate=await Source(authorize,"{\"requiredPermissions\":[],\"requiredPermissions\":[]}",delegation)) Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);
            using(var limited=await Source(authorize,"{bad",delegation)) Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);
        }
        finally { upstream.Release.TrySetResult(); File.Delete(key); }
        static Task<HttpResponseMessage> Chat(FullApplicationFixture.Client client) => client.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new { requestId=Guid.NewGuid(),body=new {message="test"} });
    }
    [Fact]
    public async Task Offline_429_can_retry_original_checkout_and_commits_one_payment_and_stock_movement()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["WindowSeconds"]="10",["Limits__OfflineSync__CallerPerWindow"]="1" });
        using var cashier=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        await cashier.JsonAsync(HttpMethod.Post,"/admin/pos/shift/open",new {openingCash=0,warehouseId=store.WarehouseId});
        var draft=await cashier.JsonAsync(HttpMethod.Post,"/admin/pos/draft");var orderId=draft.GetProperty("orderId").GetInt32();
        await cashier.JsonAsync(HttpMethod.Post,$"/admin/pos/{orderId}/items?variantId={store.VariantId}&qty=3");
        var key=Guid.NewGuid();var body=new {orderId,clientRequestId=Guid.NewGuid(),method=0,amount=60};
        async Task<HttpResponseMessage> Checkout()
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,"/admin/pos/cart/current/payment-and-finalize") {Content=JsonContent.Create(body)};
            request.Headers.Add("X-POS-Operation-Id",key.ToString());return await cashier.Http.SendAsync(request);
        }
        using(var paid=await Checkout()) Assert.Equal(HttpStatusCode.OK,paid.StatusCode);
        TimeSpan retryAfter;
        using(var limited=await Checkout()) {Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.NotNull(limited.Headers.RetryAfter);retryAfter=limited.Headers.RetryAfter!.Delta!.Value;}
        using(var status=await cashier.Http.GetAsync("/admin/pos/offline/status")) Assert.Equal(HttpStatusCode.OK,status.StatusCode);
        await Task.Delay(retryAfter+TimeSpan.FromMilliseconds(100));
        using(var retry=await Checkout()) Assert.Equal(HttpStatusCode.OK,retry.StatusCode);
        await using var check=app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.OrderPayments.Where(x=>x.OrderId==orderId).ToListAsync());
        Assert.Single(await check.Set<PosOperationReceipt>().Where(x=>x.OperationId==key).ToListAsync());
        Assert.Equal(97,(await check.InventoryBalances.SingleAsync(x=>x.ProductVariantId==store.VariantId)).OnHandQty);
    }
    [Fact]
    public async Task Valid_bank_callbacks_observe_bursts_and_duplicate_ACKs_reference_one_durable_receipt()
    {
        await using var app=await FullApplicationFixture.StartAsync();var store=app.Stores[0];
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["Limits__BankAccepted__StorePerWindow"]="1" });
        const string secret="isolated-callback-test";int bankId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            var bank=new StoreBankAccount {StoreId=store.StoreId,BankCode="ACB",BankName="TEST",AccountNumber="CALLBACK-TEST",AccountName="TEST",IsActive=true};
            db.Add(bank);await db.SaveChangesAsync();bankId=bank.Id;
        }
        // Store the test credential through the real authorized Web endpoint and its own key ring.
        using var admin=await app.LoginAsync(await app.AddAccountAsync(store,"*"));
        using(var saved=await admin.Http.PostAsync("/admin/acb/settings",new FormUrlEncodedContent(new Dictionary<string,string> {
            ["BankAccountId"]=bankId.ToString(),["Enabled"]="false",["CallbackApiKey"]=secret}))) Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
        using var callback=app.Anonymous(store);callback.Http.DefaultRequestHeaders.Add("x-api-key",secret);
        var payload=new {requestTrace="test",requestDateTime=DateTimeOffset.UtcNow,requestParameters=new {
            masterMeta=new {clientId=Guid.NewGuid(),clientRequestId=Guid.NewGuid(),checksum="test-checksum"},
            request=new {requestMeta=new {requestType="NOTIFICATION",requestCode="TRANSACTION_UPDATE"},
                requestParams=new {transactions=Array.Empty<object>(),pagination=new {page=1,pageSize=100,totalPage=1}}}}};
        var references=new List<string>();
        for(var i=0;i<4;i++)
        {
            using var response=await callback.Http.PostAsJsonAsync("/api/acb/webhook",payload);
            var text=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,text);
            using var parsed=JsonDocument.Parse(text);Assert.Equal("00000000",parsed.RootElement.GetProperty("responseStatus").GetProperty("responseCode").GetString());
            references.Add(parsed.RootElement.GetProperty("responseBody").GetProperty("referenceCode").GetString()!);
        }
        Assert.Single(references.Distinct());await using var check=app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.Set<AcbCallbackReceipt>().ToListAsync());Assert.Empty(await check.OrderPayments.ToListAsync());
    }
    private static string Delegation(RSA rsa,FullApplicationFixture.Account account)
    {
        static string Encode(byte[] value)=>Convert.ToBase64String(value).TrimEnd('=').Replace('+','-').Replace('/','_');
        var now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var header=Encode(JsonSerializer.SerializeToUtf8Bytes(new {alg="RS256",typ="gaoapp-assistant-delegation+jwt",kid="resource-test"}));
        var payload=Encode(JsonSerializer.SerializeToUtf8Bytes(new {iss="https://gao.test",sub=account.UserId.ToString(),display_name="Test",store_id=account.Store.StoreId.ToString(),permissions=new[]{AssistantPermissions.Use,AssistantPermissions.Chat},iat=now,nbf=now,exp=now+120,jti=Guid.NewGuid().ToString(),aud="gaoapp-assistant-data",azp="chatbotlocal"}));
        var content=header+"."+payload;return content+"."+Encode(rsa.SignData(System.Text.Encoding.ASCII.GetBytes(content),HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1));
    }
    [Fact]
    public async Task Phone_quota_authenticates_before_JSON_binding_and_retries_same_evidence_once()
    {
        await using var app = await FullApplicationFixture.StartBankNotificationAsync(); var store=app.Stores[0];
        // The first accepted request persists SQL evidence. A one-second window can
        // expire during that work on a busy test host, before the quota assertion.
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string> { ["WindowSeconds"]="30",["Limits__PhoneEvents__CallerPerWindow"]="1" });
        var token=new string('C',64); int deviceId;
        await using(var db=app.Database.CreateTenantContext(store.StoreId))
        {
            var bank=new StoreBankAccount {StoreId=store.StoreId,BankCode="ACB",BankName="TEST",AccountNumber="123456789",AccountName="TEST",IsActive=true};
            db.Add(bank);await db.SaveChangesAsync();
            var device=new BankNotificationDevice {StoreId=store.StoreId,BankAccountId=bank.Id,Name="Test",TokenHash=BankNotificationParser.Hash(token),BankAccountFingerprint=BankNotificationParser.AccountFingerprint(bank)};
            db.Add(device);await db.SaveChangesAsync();deviceId=device.Id;
        }
        using var phone=app.Anonymous(store);phone.Http.DefaultRequestHeaders.Add("X-Forwarded-Proto","https");
        phone.Http.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device",$"{deviceId}:{new string('D',64)}");
        using(var rejected=await phone.Http.PostAsync("/api/bank-notifications",new StringContent("{bad",System.Text.Encoding.UTF8,"application/json"))) Assert.Equal(HttpStatusCode.Unauthorized,rejected.StatusCode);
        phone.Http.DefaultRequestHeaders.Remove("X-GaoApp-Notification-Device");phone.Http.DefaultRequestHeaders.Add("X-GaoApp-Notification-Device",$"{deviceId}:{token}");
        var body=new BankNotificationUpload("mobile.acb.com.vn","Cửa hàng nhận tiền thành công +20 VND từ KHACH THU tại ACB - Chuyen tien GD RESOURCE12345 MGD: 31045",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        using(var accepted=await phone.Http.PostAsJsonAsync("/api/bank-notifications",body)) Assert.Equal(HttpStatusCode.OK,accepted.StatusCode);
        using(var limited=await phone.Http.PostAsJsonAsync("/api/bank-notifications",body)) {Assert.Equal(HttpStatusCode.TooManyRequests,limited.StatusCode);Assert.NotNull(limited.Headers.RetryAfter);Assert.DoesNotContain("accepted",await limited.Content.ReadAsStringAsync());}
        using(var heartbeat=await phone.Http.PostAsJsonAsync("/api/bank-notifications/heartbeat",new BankNotificationHeartbeat(1,0,429))) Assert.Equal(HttpStatusCode.OK,heartbeat.StatusCode);
        using(var heartbeat=await phone.Http.PostAsJsonAsync("/api/bank-notifications/heartbeat/",new BankNotificationHeartbeat(1,0,429))) Assert.Equal(HttpStatusCode.OK,heartbeat.StatusCode);
        await Task.Delay(31000);
        using(var retried=await phone.Http.PostAsJsonAsync("/api/bank-notifications",body)) Assert.Equal(HttpStatusCode.OK,retried.StatusCode);
        await using var check=app.Database.CreateTenantContext(store.StoreId);
        Assert.Single(await check.Set<BankNotificationReceipt>().ToListAsync());Assert.Empty(await check.OrderPayments.ToListAsync());
    }
    private sealed class Upstream : IDisposable
    {
        private readonly HttpListener listener=new(); private readonly CancellationTokenSource stop=new(); private readonly Task loop;
        public readonly TaskCompletionSource TwoStarted=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Block; public int Calls; private int blocked;
        public string Address {get;}
        public Upstream() {var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();var port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();Address=$"http://127.0.0.1:{port}/";listener.Prefixes.Add(Address);listener.Start();loop=Run();}
        private async Task Run() {while(!stop.IsCancellationRequested) {try {var request=await listener.GetContextAsync().WaitAsync(stop.Token);_ = Reply(request);} catch {return;}}}
        private async Task Reply(HttpListenerContext request)
        {
            try
            {
                Interlocked.Increment(ref Calls);
                if(Block && request.Request.Url!.AbsolutePath.EndsWith("/chat/messages")) {if(Interlocked.Increment(ref blocked)==2)TwoStarted.TrySetResult();await Release.Task.WaitAsync(stop.Token);}
                var token=request.Request.Headers["Authorization"]![7..].Split('.')[1];
                using var payload=JsonDocument.Parse(Convert.FromBase64String(token.Replace('-','+').Replace('_','/')+new string('=',(4-token.Length%4)%4)));
                var id=Guid.TryParse(request.Request.Headers["X-Request-Id"],out var logical)?logical:Guid.NewGuid();
                var bytes=JsonSerializer.SerializeToUtf8Bytes(new {contractVersion="1.0",requestId=id,scope=new {storeId=payload.RootElement.GetProperty("store_id").GetString(),warehouseId=(string?)null,legalEntityId=(string?)null},reply="OK"});
                request.Response.ContentType="application/json";await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
            }
            catch {try {request.Response.Abort();}catch {}}
        }
        public void Dispose() {Release.TrySetResult();stop.Cancel();listener.Close();try{loop.GetAwaiter().GetResult();}catch{}stop.Dispose();}
    }
}
