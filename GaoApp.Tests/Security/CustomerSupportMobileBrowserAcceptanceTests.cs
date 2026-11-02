using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IO.Compression;
using System.Net.Sockets;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Domain.Common;
using GaoApp.Domain.Entities;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class CustomerSupportMobileBrowserAcceptanceTests
{
    [Fact]
    public async Task Current_migration_fixture_starts_native_ui20_pages()
    {
        await using var app = await Ui20CurrentModelApplicationFixture.StartWithAuditPausedAsync();
        using var admin = await app.LoginAsync(await app.AddAccountAsync(app.Stores[0], "*"));
        foreach (var route in new[] { "/admin/customer-service", "/admin/customer-service/channels", "/admin/media-library" })
        {
            using var response = await admin.Http.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task Native_support_assistant_and_media_phone_views_keep_current_sources_scopes_drafts_and_commands()
    {
        await using var app=await Ui20CurrentModelApplicationFixture.StartWithAuditPausedAsync();
        var root=Ui20CurrentModelApplicationFixture.SourceRoot();
        var output=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-20-20261028","browser");Directory.CreateDirectory(output);
        var owned=MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-20-20261028","private-fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(owned);
        using var upstream=new Ui20AssistantSource();
        using var rsa=RSA.Create(2048);var key=Path.Combine(owned,"assistant.pem");await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());
        const string secret="UI20_SYNTHETIC_ZALO_TOKEN_NEVER_REAL";
        var token=Path.Combine(owned,"channel-token");await File.WriteAllTextAsync(token,secret);
        try {
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{["Enabled"]="true",["Issuer"]="https://ui20-fixture.test",["KeyId"]="ui20-fixture",["PrivateKeyFile"]=key,["BaseUrl"]=upstream.Address});
            var store=app.Stores[0];var manager=await app.AddAccountAsync(store,"*");
            var reader=await app.AddAccountAsync(store,CustomerServiceRights.View,PermissionCodes.Catalog.Product.View);
            var reply=await app.AddAccountAsync(store,CustomerServiceRights.View,CustomerServiceRights.Reply);
            var denied=await app.AddAccountAsync(store,PermissionCodes.Pos.Order.View);
            var foreign=await app.AddAccountAsync(app.Stores[1],"*");
            var connection=new CustomerChannelConnection{Id="ui20-zalo",StoreId=store.StoreId,Provider="zalomarketing",AccountId=Guid.NewGuid().ToString(),Enabled=true,AutoReply=false,AccessTokenFile=token,BaseUrl=upstream.Address};
            await app.ConfigureCustomerChannelsAsync(new Dictionary<string,string>{["Connections__0__Id"]=connection.Id,["Connections__0__StoreId"]=store.StoreId.ToString(),["Connections__0__Provider"]=connection.Provider,["Connections__0__AccountId"]=connection.AccountId,["Connections__0__Enabled"]="true",["Connections__0__AutoReply"]="false",["Connections__0__AccessTokenFile"]=token,["Connections__0__BaseUrl"]=upstream.Address});
            var clock=new AssistantIntegrationTests.FixedClock{Now=DateTimeOffset.UtcNow};
            var inbox=new CustomerServiceStore(new AssistantIntegrationTests.EnvironmentStub(Path.Combine(Path.GetDirectoryName(app.CustomerServiceDatabasePath)!,"app")),new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new(){DatabasePath=app.CustomerServiceDatabasePath}),clock);
            inbox.Configure(store.StoreId,true,inbox.Settings(store.StoreId).Version);inbox.Configure(app.Stores[1].StoreId,true,inbox.Settings(app.Stores[1].StoreId).Version);
            var staff=new CustomerStaff(manager.UserId,store.StoreId,"UI20 nhân viên",true,true,true);
            CustomerConversation Conversation(int storeId,string title,bool waiting=false) {
                var visitor=Guid.NewGuid();var c=inbox.Start(storeId,visitor,new(Guid.NewGuid(),title,"UI20 liên hệ tự khai",true));
                var turn=Guid.NewGuid();inbox.Begin(storeId,visitor,c.Id,new(turn,"UI20 hỏi về sản phẩm và điều kiện mua hàng"));
                c=inbox.Complete(new(visitor,storeId,c.Id,turn),"UI20 câu trả lời mẫu nội bộ. Dữ liệu này chỉ thuộc fixture, không gửi tới khách thật.","{}",null);
                if(waiting)c=inbox.Handoff(storeId,visitor,c.Id,Guid.NewGuid());return c;
            }
            var waiting=Conversation(store.StoreId,"UI20 khách chờ hỗ trợ · tên dài trên điện thoại",true);
            var human=Conversation(store.StoreId,"UI20 khách do nhân viên tiếp nhận",true);
            human=inbox.StaffCommand(staff,human.Id,new(Guid.NewGuid(),"claim",human.Version));
            var closed=Conversation(store.StoreId,"UI20 hội thoại đã kết thúc",true);
            closed=inbox.StaffCommand(staff,closed.Id,new(Guid.NewGuid(),"close",closed.Version));
            var foreignConversation=Conversation(app.Stores[1].StoreId,"UI20_FOREIGN_PRIVATE_CUSTOMER",true);
            var feedbackMessage=waiting.Messages.Last(m=>m.Role=="assistant");
            inbox.RateStaff(staff,waiting.Id,new(Guid.NewGuid(),feedbackMessage.Id,"wrong_price"));
            var zalo=inbox.ImportChannel(connection,new("ui20-local-event",Guid.NewGuid().ToString(),"UI20 bản nháp tệp nội bộ",clock.Now,"ui20-local-reference",NeedsHuman:true,DisplayName:"UI20 file draft",ConversationKind:"Direct"))!.Value;
            var zaloRow=inbox.StaffGet(staff,zalo);inbox.StaffCommand(staff,zalo,new(Guid.NewGuid(),"claim",zaloRow.Version));
            using var admin=await app.LoginAsync(manager);using var view=await app.LoginAsync(reader);using var no=await app.LoginAsync(denied);using var other=await app.LoginAsync(foreign);
            var web=TestApplicationBuild.WebAssemblyPath();
            var compiled=System.Reflection.Assembly.LoadFrom(web).GetCustomAttributesData()
                .Where(attribute=>attribute.AttributeType.FullName=="Microsoft.AspNetCore.Razor.Hosting.RazorCompiledItemAttribute")
                .Select(attribute=>attribute.ConstructorArguments[2].Value?.ToString()).Where(value=>value is not null).ToHashSet();
            var expectedViews=new[]{"AssistantKnowledge/Index","AssistantQuality/Index","AssistantSearch/Index","AssistantSettings/Index","CustomerChannels/Index","CustomerInbox/Index","CustomerQuality/Index","Media/Test","MediaLibrary/Index","Shared/Components/Assistant/Default"}.Select(name=>"/Areas/Admin/Views/"+name+".cshtml").ToArray();
            Assert.All(expectedViews,identifier=>Assert.Contains(identifier,compiled));
            await File.WriteAllTextAsync(Path.Combine(output,"compiled-views.json"),JsonSerializer.Serialize(new{passed=true,expectedViews,webAssembly=web,webAssemblySha256=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(web))).ToLowerInvariant(),compiledItemCount=compiled.Count},new JsonSerializerOptions{WriteIndented=true}));
            var expectedAssetsPath=Environment.GetEnvironmentVariable("GAOAPP_UI20_EXPECTED_ASSETS");
            if(!string.IsNullOrWhiteSpace(expectedAssetsPath)) {
                var expectedAssets=JsonDocument.Parse(await File.ReadAllTextAsync(expectedAssetsPath)).RootElement.GetProperty("assets");var checks=new List<object>();
                foreach(var asset in expectedAssets.EnumerateArray())foreach(var requestedEncoding in new[]{"identity","gzip"}) {
                    var url=asset.GetProperty("url").GetString()!;var expectedSha256=asset.GetProperty("sha256").GetString()!;
                    using var request=new HttpRequestMessage(HttpMethod.Get,url);request.Headers.AcceptEncoding.ParseAdd(requestedEncoding);
                    using var response=await admin.Http.SendAsync(request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var content=await response.Content.ReadAsByteArrayAsync();
                    var actualEncoding=response.Content.Headers.ContentEncoding.ToArray();Assert.All(actualEncoding,encoding=>Assert.Equal("gzip",encoding));
                    if(actualEncoding.Contains("gzip")){using var stream=new MemoryStream(content);using var zip=new GZipStream(stream,CompressionMode.Decompress);using var decoded=new MemoryStream();await zip.CopyToAsync(decoded);content=decoded.ToArray();}
                    var actualSha256=Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();Assert.Equal(expectedSha256,actualSha256);
                    checks.Add(new{url,requestedEncoding,actualEncoding,expectedSha256,actualSha256});
                }
                await File.WriteAllTextAsync(Path.Combine(output,"served-assets.json"),JsonSerializer.Serialize(new{passed=true,manifest=expectedAssetsPath,checks},new JsonSerializerOptions{WriteIndented=true}));
            }
            await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
                var product=await db.Products.SingleAsync(p=>p.Id==db.ProductVariants.Where(v=>v.Id==store.VariantId).Select(v=>v.ProductId).Single());
                product.Name="UI20 sản phẩm kiểm thử tên dài · sữa và ngũ cốc nguyên hạt";await db.SaveChangesAsync();
            }
            async Task<int> Upload(string name) {
                using var body=new MultipartFormDataContent();var image=new ByteArrayContent(Convert.FromBase64String(Png));image.Headers.ContentType=new MediaTypeHeaderValue("image/png");body.Add(image,"file",name);
                using var response=await admin.Http.PostAsync("/admin/media/temp",body);Assert.Equal(HttpStatusCode.OK,response.StatusCode);var uploaded=await response.Content.ReadAsStringAsync();
                await using var db=app.Database.CreateTenantContext(store.StoreId);return await db.MediaAssets.Where(a=>a.TempToken==uploaded).Select(a=>a.Id).SingleAsync();
            }
            var mediaUsed=await Upload("UI20-used-image.png");var mediaTemp=await Upload("UI20-temp-image.png");
            await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
                var asset=await db.MediaAssets.SingleAsync(a=>a.Id==mediaUsed);asset.IsTemp=false;asset.TempToken=null;asset.ExpireAtUtc=null;
                db.ProductImages.Add(new ProductImage{StoreId=store.StoreId,ProductId=await db.ProductVariants.Where(v=>v.Id==store.VariantId).Select(v=>v.ProductId).SingleAsync(),MediaAssetId=mediaUsed});await db.SaveChangesAsync();
            }
            var forbidden=new List<object>();
            foreach(var route in new[]{"/admin/customer-service","/admin/customer-service/channels","/admin/customer-service/quality","/admin/media-library","/admin/media/test","/admin/assistant/settings","/admin/assistant/knowledge","/admin/assistant/quality","/admin/assistant/search"}) {
                using var response=await no.Http.GetAsync(route);Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);forbidden.Add(new{method="GET",route,status=403});
            }
            using(var response=await other.Http.GetAsync("/admin/customer-service/conversations/"+waiting.Id))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
            using(var response=await other.Http.GetAsync("/admin/media-library/"+mediaUsed+"/preview"))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
            using(var response=await view.Http.PostAsJsonAsync("/admin/customer-service/conversations/"+waiting.Id+"/commands",new{requestId=Guid.NewGuid(),action="claim",version=waiting.Version}))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            var before=await SqlState(app);var beforeMedia=await MediaState(app);var settings=inbox.Settings(store.StoreId);
            var ids=new{waiting=waiting.Id,human=human.Id,closed=closed.Id,foreign=foreignConversation.Id,zalo};
            object Account(Ui20CurrentModelApplicationFixture.Account account)=>new{user=account.Name,password=account.Password};
            var start=new ProcessStartInfo("node"){WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
            start.Environment["NODE_PATH"]=Path.Combine(root,"Logs","pos-offline-browser-deps","node_modules");
            start.ArgumentList.Add(Path.Combine(root,"GaoApp.Tests.Browser","support-mobile.browser.cjs"));
            using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{baseUrl=$"http://{store.Host}:{app.Address.Port}",storeId=store.StoreId,variantId=store.VariantId,terminalId=store.TerminalId,output,ownedFixture=true,accounts=new{admin=Account(manager),reader=Account(reader),reply=Account(reply),noRights=Account(denied)},ids,mediaUsedAssetId=mediaUsed,mediaTempAssetId=mediaTemp,assistantProductQuery="UI20",assistantPendingChat=false,secretMarkers=new[]{secret,key,token},foreignMarker="UI20_FOREIGN_PRIVATE_CUSTOMER",inboxReplyText=ReplyText}));
            process.StandardInput.Close();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(18));
            try{await process.WaitForExitAsync(timeout.Token);}catch{if(!process.HasExited)process.Kill(true);throw;}
            var log=await stdout+"\n"+await stderr;await File.WriteAllTextAsync(Path.Combine(output,"chrome.log"),log);Assert.True(process.ExitCode==0,log);
            Assert.Equal(before,await SqlState(app));Assert.Equal(settings,inbox.Settings(store.StoreId));
            var final=inbox.StaffGet(staff,waiting.Id);Assert.Single(final.Messages,m=>m.Role=="agent"&&m.Text==ReplyText);
            Assert.DoesNotContain(inbox.StaffGet(staff,zalo).Messages,m=>m.Role=="agent");
            Assert.Equal(beforeMedia,await MediaState(app,excludeBrowserUploads:true));
            await using(var db=app.Database.CreateTenantContext(store.StoreId)) {
                var uploads=await db.MediaAssets.Where(a=>a.OriginalFileName=="ui20-browser-upload-1.png"||a.OriginalFileName=="ui20-browser-upload-2.png").ToListAsync();
                Assert.Equal(2,uploads.Count);Assert.All(uploads,a=>{Assert.True(a.IsTemp);Assert.Null(a.TempToken);Assert.True(a.ExpireAtUtc<=DateTime.UtcNow);});
            }
            var result=JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(output,"support-mobile-result.json"))).RootElement;
            Assert.True(result.GetProperty("passed").GetBoolean());Assert.True(result.GetProperty("nativeStateCount").GetInt32()>=30);
            await File.WriteAllTextAsync(Path.Combine(output,"sql-checks.json"),JsonSerializer.Serialize(new{passed=true,schemaMode="strict_MigrateAsync",migrationAcceptanceImplied=true,wholeCurrentApplicationSources=true,originalMigrationFixtureUnchanged=true,beforeSha256=Hash(before),afterSha256=Hash(await SqlState(app)),storeMenuGrantsConfigurationAndFinancialDataUnchanged=true,preexistingMediaUnchanged=true,browserTempUploads=2,revertedTempUploads=2,ownedWebReplyCount=1,zaloReplyCount=0,foreignRecordsExcluded=true,forbiddenChecks=forbidden,externalAiOrChannelCalls=0,upstreamPaths=upstream.Paths.ToArray()},new JsonSerializerOptions{WriteIndented=true}));
        }finally {
            var allowed=Path.GetFullPath(MobileAcceptanceEvidence.PathFor(root,"TestResults","mobile-ui-20-20261028","private-fixtures"))+Path.DirectorySeparatorChar;
            Assert.StartsWith(allowed,Path.GetFullPath(owned));if(Directory.Exists(owned))Directory.Delete(owned,true);
        }
    }

    private const string ReplyText="UI20 browser · trả lời đúng một lần";
    private const string Png="iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+ip1sAAAAASUVORK5CYII=";
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static async Task<string> SqlState(Ui20CurrentModelApplicationFixture app) {
        await using var db=app.Database.CreateHostContext();
        async Task<object> Rows<T>() where T:BaseEntity {var rows=await db.Set<T>().IgnoreQueryFilters().AsNoTracking().OrderBy(x=>x.Id).ToListAsync();return rows.Select(row=>db.Entry(row).Properties.OrderBy(p=>p.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)).ToList();}
        return JsonSerializer.Serialize(new{stores=await Rows<Store>(),menus=await Rows<AdminMenuItem>(),visibility=await Rows<AdminMenuVisibilitySetting>(),pins=await Rows<AdminMenuPinSetting>(),roles=await Rows<Role>(),grants=await db.RolePermissions.OrderBy(x=>x.Id).Select(x=>new{x.Id,x.RoleId,x.PermissionId}).ToListAsync(),products=await Rows<Product>(),variants=await Rows<ProductVariant>(),balances=await Rows<InventoryBalance>(),payments=await Rows<OrderPayment>()});
    }
    private static async Task<string> MediaState(Ui20CurrentModelApplicationFixture app,bool excludeBrowserUploads=false) {
        await using var db=app.Database.CreateHostContext();var query=db.MediaAssets.IgnoreQueryFilters().AsNoTracking();
        if(excludeBrowserUploads)query=query.Where(a=>a.OriginalFileName!="ui20-browser-upload-1.png"&&a.OriginalFileName!="ui20-browser-upload-2.png");
        var rows=await query.OrderBy(a=>a.Id).ToListAsync();return JsonSerializer.Serialize(rows.Select(row=>db.Entry(row).Properties.OrderBy(p=>p.Metadata.Name).ToDictionary(p=>p.Metadata.Name,p=>p.CurrentValue)));
    }

    private sealed class Ui20AssistantSource:IDisposable {
        private readonly HttpListener listener=new();private readonly CancellationTokenSource stop=new();private readonly Task loop;
        internal string Address{get;}internal System.Collections.Concurrent.ConcurrentQueue<string> Paths{get;}=new();
        private static readonly Guid ConversationId=Guid.Parse("00000000-0000-4000-8000-000000000020");
        internal Ui20AssistantSource(){var tcp=new TcpListener(IPAddress.Loopback,0);tcp.Start();var port=((IPEndPoint)tcp.LocalEndpoint).Port;tcp.Stop();Address=$"http://127.0.0.1:{port}/";listener.Prefixes.Add(Address);listener.Start();loop=Serve();}
        private async Task Serve(){while(!stop.IsCancellationRequested){HttpListenerContext http;try{http=await listener.GetContextAsync().WaitAsync(stop.Token);}catch{return;}try{
            var path=http.Request.Url!.AbsolutePath;Paths.Enqueue(path);
            var jwt=http.Request.Headers["Authorization"]![7..].Split('.')[1];var payload=JsonDocument.Parse(Convert.FromBase64String(jwt.Replace('-','+').Replace('_','/')+new string('=',(4-jwt.Length%4)%4))).RootElement;
            var user=payload.GetProperty("sub").GetString();var store=payload.GetProperty("store_id").GetString();
            var scope=new{storeId=store,warehouseId=payload.TryGetProperty("warehouse_id",out var w)?w.GetString():null,legalEntityId=payload.TryGetProperty("legal_entity_id",out var l)?l.GetString():null};
            var requestId=Guid.TryParse(http.Request.Headers["X-Request-Id"],out var id)?id:Guid.NewGuid();
            object result=new{contractVersion="1.0",requestId,scope,conversationId=ConversationId,title="UI20 hội thoại trợ lý kiểm thử",reply="UI20 nội dung giả lập riêng, không gọi AI bên ngoài."};
            if(path.EndsWith("/capabilities"))result=new{contractVersion="1.0",requestId,scope,chat=new{available=true},features=new{chat=true}};
            else if(path.EndsWith("/conversations")&&http.Request.HttpMethod=="GET")result=new{contractVersion="1.0",requestId,scope,items=new[]{new{conversationId=ConversationId,title="UI20 lịch sử trợ lý",scope,updatedAt=DateTimeOffset.UtcNow}},nextCursor=(string?)null};
            else if(path.EndsWith("/messages")&&http.Request.HttpMethod=="GET")result=new{contractVersion="1.0",requestId,scope,conversationId=ConversationId,items=new object[]{new{messageId="ui20-user-1",role="user",content="UI20 tìm sản phẩm trong cửa hàng",scope},new{messageId="ui20-assistant-1",role="assistant",content="UI20 phản hồi mẫu đủ dài để kiểm tra cách xuống dòng, giữ phạm vi cửa hàng và chức năng góp ý khi sử dụng trên điện thoại.",scope,expiresAt=DateTimeOffset.UtcNow.AddMinutes(30)}},feedback=Array.Empty<object>(),nextCursor=(string?)null};
            else if(path.EndsWith("/operations/summary"))result=new{contractVersion="1.0",requestId,scope,report=new{generatedAt=DateTimeOffset.UtcNow,from=DateTimeOffset.UtcNow.AddDays(-1),retentionDays=7,scopedSince=DateTimeOffset.UtcNow.AddHours(-1),requests=new{attempts=2,completed=2,accepted=0,failed=0,cancelled=0,p50Ms=3,p95Ms=10,latencySamples=2,sampleLimited=false},ai=new[]{new{provider="UI20 fake",model="UI20 local model",project="Owned fixture",slot=1,attempts=2,succeeded=2,failed=0,interrupted=0,knownTokens=(long?)12,unknownTokens=0,cooldownKind=(string?)null,cooldownUntil=(DateTimeOffset?)null}},errors=Array.Empty<object>(),openFeedback=1,resolvedFeedback=0,feedback=new[]{new{id=Guid.Parse("00000000-0000-4000-8000-000000000021"),owner=user,conversationId=ConversationId,messageId="ui20-assistant-1",kind="wrong_price",status="open",version="ui20-feedback-v1",updatedAt=DateTimeOffset.UtcNow}}}};
            var bytes=JsonSerializer.SerializeToUtf8Bytes(result);http.Response.ContentType="application/json";http.Response.ContentLength64=bytes.Length;await http.Response.OutputStream.WriteAsync(bytes);http.Response.Close();
        }catch(Exception){http.Response.StatusCode=500;http.Response.Close();}}}
        public void Dispose(){stop.Cancel();listener.Close();try{loop.GetAwaiter().GetResult();}catch{}stop.Dispose();}
    }
}
