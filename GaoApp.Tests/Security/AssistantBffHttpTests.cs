using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Tests.Security;

public sealed class AssistantBffHttpTests
{
    [Fact]
    public async Task Cookie_csrf_scope_permission_recheck_and_feature_off_preserve_manual_app()
    {
        await using var app=await FullApplicationFixture.StartAsync();
        var rights=new[]{AssistantPermissions.Use,AssistantPermissions.Chat,AssistantPermissions.Retail,AssistantPermissions.Receiving,AssistantPermissions.NormalizeName,
            "inventory.stockdocument.create",PermissionCodes.Catalog.Product.Create,PermissionCodes.Catalog.Product.View,PermissionCodes.System.Setting.Update};
        var first=await app.AddAccountAsync(app.Stores[0],rights);
        var second=await app.AddAccountAsync(app.Stores[1],rights.Where(r=>r!=PermissionCodes.System.Setting.Update).ToArray());
        using var own=await app.LoginAsync(first);using var other=await app.LoginAsync(second);
        var disabled=await own.Http.GetAsync("/admin/api/assistant/context");Assert.Equal(HttpStatusCode.ServiceUnavailable,disabled.StatusCode);
        Assert.DoesNotContain("id=\"gaoAssistant\"",await own.Http.GetStringAsync("/admin/media-library"));
        using var upstream=new FakeUpstream();using var rsa=RSA.Create(2048);
        var keyFile=Path.Combine(Path.GetTempPath(),"gao-bff-"+Guid.NewGuid()+".pem");await File.WriteAllTextAsync(keyFile,rsa.ExportRSAPrivateKeyPem());
        try{
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{{"Enabled","true"},{"BaseUrl",upstream.Address},{"Issuer","https://gaoapp.bff.test"},{"KeyId","bff-test"},{"PrivateKeyFile",keyFile}});
            var context=await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/context");Assert.Equal(first.UserId.ToString(),context.GetProperty("identity").GetString());
            Assert.Equal(first.Store.StoreId.ToString(),context.GetProperty("scope").GetProperty("storeId").GetString());
            await using (var db = app.Database.CreateTenantContext(first.Store.StoreId)) {
                var name = await db.Stores.IgnoreQueryFilters().Where(s => s.Id == first.Store.StoreId).Select(s => s.Name).SingleAsync();
                Assert.Equal(name, context.GetProperty("storeName").GetString());
                var spoofed = await own.Http.GetFromJsonAsync<JsonElement>($"/admin/api/assistant/context?storeId={second.Store.StoreId}");
                Assert.Equal(name, spoofed.GetProperty("storeName").GetString());
                Assert.Equal(first.Store.StoreId.ToString(), spoofed.GetProperty("scope").GetProperty("storeId").GetString());
            }
            var otherContext=await other.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/context");Assert.Equal(second.Store.StoreId.ToString(),otherContext.GetProperty("scope").GetProperty("storeId").GetString());
            var productScreen=await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/context?screen=product_create");
            Assert.True(productScreen.GetProperty("screen").GetProperty("canApply").GetBoolean());
            Assert.False(context.TryGetProperty("screen",out _)); // Optional context keeps older consumers compatible.
            int receiptId;
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                var variant=await db.ProductVariants.Include(v=>v.Product).SingleAsync(v=>v.Id==first.Store.VariantId);
                db.ProductUnitConversions.Add(new GaoApp.Domain.Entities.ProductUnitConversion {StoreId=first.Store.StoreId,
                    ProductVariantId=variant.Id,UnitId=variant.Product.BaseUnitId,Factor=1,IsBaseUnit=true,IsActive=true,Price=20});
                var doc=new GaoApp.Domain.Entities.StockDocument {StoreId=first.Store.StoreId,WarehouseId=first.Store.WarehouseId,
                    DocumentNo="SCREEN-"+Guid.NewGuid().ToString("N"),Status=GaoApp.Domain.Enums.StockDocumentStatus.Draft};
                db.StockDocuments.Add(doc);await db.SaveChangesAsync();receiptId=doc.Id;
            }
            var receiptScreen=await own.Http.GetFromJsonAsync<JsonElement>($"/admin/api/assistant/context?screen=receiving&documentId={receiptId}");
            Assert.True(receiptScreen.GetProperty("screen").GetProperty("canApply").GetBoolean());
            Assert.Equal(receiptId,receiptScreen.GetProperty("screen").GetProperty("documentId").GetInt32());
            var unitUrl=$"/admin/api/assistant/receiving/{receiptId}/products/{first.Store.VariantId}/units?documentVersion="+Uri.EscapeDataString(receiptScreen.GetProperty("screen").GetProperty("documentVersion").GetString()!);
            var actualUnits=await own.Http.GetFromJsonAsync<JsonElement>(unitUrl);
            Assert.Equal(receiptId,actualUnits.GetProperty("documentId").GetInt32());Assert.NotEmpty(actualUnits.GetProperty("items").EnumerateArray());
            Assert.All(actualUnits.GetProperty("items").EnumerateArray(),u=>{Assert.Equal(first.Store.VariantId,u.GetProperty("productVariantId").GetInt32());Assert.False(u.TryGetProperty("costPrice",out _));});
            Assert.Equal(HttpStatusCode.NotFound,(await other.Http.GetAsync(unitUrl)).StatusCode);
            var foreignUnitUrl=unitUrl.Replace($"/products/{first.Store.VariantId}/",$"/products/{second.Store.VariantId}/");
            Assert.Equal(HttpStatusCode.NotFound,(await own.Http.GetAsync(foreignUnitUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,(await own.Http.GetAsync(unitUrl.Replace("documentVersion=","documentVersion=invalid"))).StatusCode);
            int retailPermissionId;
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                retailPermissionId=await db.Permissions.Where(p=>p.Code==AssistantPermissions.Retail).Select(p=>p.Id).SingleAsync();
                db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(p=>p.RoleId==first.RoleId&&p.PermissionId==retailPermissionId));await db.SaveChangesAsync();
            }
            var hiddenPrices=await own.Http.GetFromJsonAsync<JsonElement>(unitUrl);
            Assert.All(hiddenPrices.GetProperty("items").EnumerateArray(),u=>{Assert.Equal(JsonValueKind.Null,u.GetProperty("price").ValueKind);Assert.Equal("forbidden",u.GetProperty("priceStatus").GetString());});
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                db.RolePermissions.Add(new GaoApp.Domain.Entities.RolePermission {RoleId=first.RoleId,PermissionId=retailPermissionId});await db.SaveChangesAsync();
            }
            Assert.Equal(HttpStatusCode.NotFound,(await other.Http.GetAsync($"/admin/api/assistant/context?screen=receiving&documentId={receiptId}")).StatusCode);
            var screenCalls=upstream.Calls;
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new{requestId=Guid.NewGuid(),screen="receiving",documentId=receiptId,body=new{message="test"}}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Equal(screenCalls,upstream.Calls);
            var receivingPayload=new{requestId=Guid.NewGuid(),screen="receiving",documentId=receiptId,body=new{originalText="TEST",text="TEST",generation=1,inputSource="typed"}};
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Receiving",receivingPayload))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            upstream.BeforeReply=async()=>{await using var db=app.Database.CreateTenantContext(first.Store.StoreId);
                var doc=await db.StockDocuments.SingleAsync(d=>d.Id==receiptId);doc.DocumentTitle="Changed during AI response";await db.SaveChangesAsync();};
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Receiving",receivingPayload)){
                Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
                Assert.Equal("receiving_document_changed",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            }
            upstream.BeforeReply=null;
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                var doc=await db.StockDocuments.SingleAsync(d=>d.Id==receiptId);doc.Status=GaoApp.Domain.Enums.StockDocumentStatus.PendingApproval;await db.SaveChangesAsync();
            }
            screenCalls=upstream.Calls;
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Receiving",receivingPayload)){
                Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
                Assert.Equal("receiving_document_locked",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
            }
            Assert.Equal(screenCalls,upstream.Calls);
            var lockedScreen=await own.Http.GetFromJsonAsync<JsonElement>($"/admin/api/assistant/context?screen=receiving&documentId={receiptId}");
            Assert.False(lockedScreen.GetProperty("screen").GetProperty("canApply").GetBoolean());
            Assert.Equal(HttpStatusCode.Conflict,(await own.Http.GetAsync(unitUrl)).StatusCode);
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)) Assert.Empty(await db.StockDocumentLines.Where(l=>l.StockDocumentId==receiptId).ToArrayAsync());
            Assert.Contains("id=\"gaoAssistant\"",await own.Http.GetStringAsync("/admin/media-library"));
            var quality=await own.Http.GetStringAsync("/admin/assistant/quality?days=7&storeId="+second.Store.StoreId);
            Assert.Contains("Chất lượng trợ lý",quality);Assert.Contains("TEST-model",quality);Assert.Equal(first.Store.StoreId.ToString(),upstream.LastStore);
            var qualityOutput=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","assistant-upgrade-step6");Directory.CreateDirectory(qualityOutput);
            await File.WriteAllTextAsync(Path.Combine(qualityOutput,"quality-page.html"),quality);
            Assert.Contains(AssistantPermissions.Monitor,upstream.LastPermissions);Assert.DoesNotContain(AssistantPermissions.Chat,upstream.LastPermissions);
            Assert.True((await other.Http.GetAsync("/admin/assistant/quality")).StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
            string QualityField(string name)=>System.Text.RegularExpressions.Regex.Match(quality,"name=\""+name+"\" value=\"([^\"]+)\"").Groups[1].Value;
            var resolveRequest=Guid.NewGuid();var resolveInput=new Dictionary<string,string>{{"id",QualityField("id")},{"version",QualityField("version")},{"requestId",resolveRequest.ToString()},{"days","7"}};
            var qualityCsrf=own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            using(var response=await own.Http.PostAsync("/admin/assistant/quality/resolve",new FormUrlEncodedContent(resolveInput)))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            own.Http.DefaultRequestHeaders.Add("RequestVerificationToken",qualityCsrf);
            using(var response=await own.Http.PostAsync("/admin/assistant/quality/resolve",new FormUrlEncodedContent(resolveInput)))Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
            Assert.EndsWith("/resolve",upstream.LastUrl);Assert.Equal(resolveRequest,upstream.LastRequestId);
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Monitor",new{}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            var settingsPage = await own.Http.GetStringAsync("/admin/assistant/settings");
            Assert.Contains("Quản lý AI", settingsPage);
            var deniedSettings = await other.Http.GetAsync("/admin/assistant/settings");
            Assert.True(deniedSettings.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
            string Version(string html) => System.Text.RegularExpressions.Regex.Match(html, "name=\"Version\" value=\"([^\"]+)\"").Groups[1].Value;
            var initialVersion = Version(settingsPage); Assert.Equal("initial", initialVersion);
            async Task<HttpResponseMessage> SaveSwitches(string version, bool enabled, bool chat) => await own.Http.PostAsync("/admin/assistant/settings",
                new FormUrlEncodedContent(new Dictionary<string,string> { ["Version"] = version, ["Enabled"] = enabled.ToString(),
                    ["ChatEnabled"] = chat.ToString(), ["ReceivingEnabled"] = "true", ["NormalizeEnabled"] = "true",
                    ["StoreId"] = second.Store.StoreId.ToString() }));
            using(var response = await SaveSwitches(initialVersion, true, false)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var withoutChat = await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/context");
            Assert.DoesNotContain(AssistantPermissions.Chat, withoutChat.GetProperty("permissions").EnumerateArray().Select(p=>p.GetString()));
            Assert.Contains(AssistantPermissions.Chat, (await other.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/context")).GetProperty("permissions").EnumerateArray().Select(p=>p.GetString()));
            using(var response = await SaveSwitches(initialVersion, true, true)) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            var currentVersion = Version(await own.Http.GetStringAsync("/admin/assistant/settings"));
            using(var response = await SaveSwitches(currentVersion, false, true)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await own.Http.GetAsync("/admin/api/assistant/context")).StatusCode);
            Assert.DoesNotContain("id=\"gaoAssistant\"", await own.Http.GetStringAsync("/admin/media-library"));
            Assert.Contains("TEST-model",await own.Http.GetStringAsync("/admin/assistant/quality"));
            Assert.Equal(HttpStatusCode.OK, (await own.Http.GetAsync("/admin/assistant/settings")).StatusCode);
            currentVersion = Version(await own.Http.GetStringAsync("/admin/assistant/settings"));
            var settingsCsrf = own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();
            own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            using(var response = await SaveSwitches(currentVersion, true, true)) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            own.Http.DefaultRequestHeaders.Add("RequestVerificationToken", settingsCsrf);
            using(var response = await SaveSwitches(currentVersion, true, true)) Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var id=Guid.NewGuid();var payload=new{requestId=id,body=new{message="giá sữa"}};
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",payload))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Equal(first.UserId.ToString(),upstream.LastUser);Assert.Equal(first.Store.StoreId.ToString(),upstream.LastStore);Assert.Equal(id,upstream.LastRequestId);
            Assert.Equal("gaoapp-assistant-context+jwt",upstream.ContextType);Assert.Equal("gaoapp-assistant-delegation+jwt",upstream.DelegationType);
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/History",new{resourceId=Guid.NewGuid(),limit=50,cursor="a&storeId=2",includeClarification=true})){
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);
                var history=await response.Content.ReadFromJsonAsync<JsonElement>();Assert.Equal("sugar",history.GetProperty("clarification").GetProperty("field").GetString());
                Assert.Equal("Có đường",history.GetProperty("clarification").GetProperty("options")[0].GetString());
            }
            Assert.EndsWith("cursor=a%26storeId%3D2&includeClarification=true",upstream.LastUrl);
            Assert.Equal(first.Store.StoreId.ToString(),upstream.LastStore);
            string sourceSku;CatalogProduct sourceProduct;
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                var variant=await db.ProductVariants.Include(v=>v.Product).ThenInclude(p=>p.BaseUnit).SingleAsync(v=>v.Id==first.Store.VariantId);
                sourceSku=variant.Sku;sourceProduct=AssistantCatalogService.Build(variant,false).Product;
            }
            upstream.ProductDisplay=new{messageId="12",scope=new{storeId=first.Store.StoreId.ToString(),warehouseId=(string?)null,legalEntityId=(string?)null},kind="quote",
                items=new[]{new{product=sourceProduct,choice=(int?)null}},expiresAt=DateTimeOffset.UtcNow.AddMinutes(30),hasMore=false,priceReadAt=DateTimeOffset.UtcNow,priceTiers=new[]{"retail"}};
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/History",new{resourceId=Guid.NewGuid(),includeProducts=true})){
                Assert.Equal(HttpStatusCode.OK,response.StatusCode);var history=await response.Content.ReadFromJsonAsync<JsonElement>();
                var card=history.GetProperty("productDisplay");Assert.Equal(sourceSku,card.GetProperty("items")[0].GetProperty("sku").GetString());
                Assert.Equal(sourceProduct.ProductId,card.GetProperty("items")[0].GetProperty("product").GetProperty("productId").GetString());
                Assert.False(card.GetProperty("canReadWholesale").GetBoolean());Assert.False(card.GetProperty("items")[0].TryGetProperty("price",out _));
            }
            Assert.EndsWith("includeProducts=true",upstream.LastUrl);
            var beforeInvalidHints=upstream.Calls;
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new{requestId=Guid.NewGuid(),includeClarification=true,body=new{message="a"}}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Equal(beforeInvalidHints,upstream.Calls);
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new{requestId=Guid.NewGuid(),includeProducts=true,body=new{message="a"}}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            Assert.Equal(beforeInvalidHints,upstream.Calls);
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new{requestId=Guid.NewGuid(),storeId=second.Store.StoreId,body=new{message="a"}}))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            var csrf=own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",payload))Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
            own.Http.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
            upstream.WrongScope=true;
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",new{requestId=Guid.NewGuid(),body=new{message="a"}}))Assert.Equal(HttpStatusCode.BadGateway,response.StatusCode);
            upstream.WrongScope=false;
            await using(var db=app.Database.CreateTenantContext(first.Store.StoreId)){
                var permission=await db.Permissions.SingleAsync(p=>p.Code==AssistantPermissions.Chat);
                db.RolePermissions.Remove(await db.RolePermissions.SingleAsync(p=>p.RoleId==first.RoleId&&p.PermissionId==permission.Id));await db.SaveChangesAsync();
            }
            var calls=upstream.Calls;
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Chat",payload))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            Assert.Equal(calls,upstream.Calls);
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{{"Enabled","false"}});
            using(var response=await own.Http.PostAsJsonAsync("/admin/api/assistant/call/Capabilities",new{}))Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await own.Http.GetAsync("/admin/media-library")).StatusCode);
        }finally{File.Delete(keyFile);}
    }
    private sealed class FakeUpstream:IDisposable
    {
        private readonly HttpListener listener=new();private readonly CancellationTokenSource shutdown=new();private readonly Task loop;
        internal string Address{get;}internal string? LastUser,LastStore,ContextType,DelegationType,LastUrl;internal Guid LastRequestId;internal bool WrongScope;internal int Calls;internal object? ProductDisplay;internal Func<Task>? BeforeReply;internal string[] LastPermissions=[];
        internal FakeUpstream(){var port=new TcpListener(IPAddress.Loopback,0);port.Start();var number=((IPEndPoint)port.LocalEndpoint).Port;port.Stop();Address=$"http://127.0.0.1:{number}/";listener.Prefixes.Add(Address);listener.Start();loop=Serve();}
        private async Task Serve(){while(!shutdown.IsCancellationRequested){HttpListenerContext request;try{request=await listener.GetContextAsync().WaitAsync(shutdown.Token);}catch{return;}
                try{Calls++;LastUrl=request.Request.RawUrl;var context=Parts(request.Request.Headers["Authorization"]![7..]);var delegation=Parts(request.Request.Headers["X-GaoApp-Delegation"]!);
                    ContextType=context.Header.GetProperty("typ").GetString();DelegationType=delegation.Header.GetProperty("typ").GetString();LastUser=context.Body.GetProperty("sub").GetString();LastStore=context.Body.GetProperty("store_id").GetString();LastRequestId=Guid.TryParse(request.Request.Headers["X-Request-Id"],out var logical)?logical:Guid.NewGuid();
                    var scope=new{storeId=WrongScope?"wrong":LastStore,warehouseId=context.Body.TryGetProperty("warehouse_id",out var w)?w.GetString():null,legalEntityId=context.Body.TryGetProperty("legal_entity_id",out var l)?l.GetString():null};
                    LastPermissions=context.Body.GetProperty("permissions").EnumerateArray().Select(p=>p.GetString()!).ToArray();
                    if(BeforeReply is not null)await BeforeReply();
                    object reply=request.Request.Url!.AbsolutePath.EndsWith("/messages")&&request.Request.HttpMethod=="GET"
                        ?new{contractVersion="1.0",requestId=LastRequestId,scope,conversationId=Guid.NewGuid(),items=new[]{new{messageId="12",role="assistant",content="TEST",scope}},nextCursor=(string?)null,productDisplay=ProductDisplay,
                            clarification=new{messageId="12",scope,field="sugar",options=new[]{"Có đường","Ít đường"},expiresAt=DateTimeOffset.UtcNow.AddMinutes(30)}}
                        :new{contractVersion="1.0",requestId=LastRequestId,scope,conversationId=Guid.NewGuid(),reply="TEST"};
                    if(request.Request.Url.AbsolutePath.EndsWith("/operations/summary"))reply=new{contractVersion="1.0",requestId=LastRequestId,scope,report=new{
                        generatedAt=DateTimeOffset.UtcNow,from=DateTimeOffset.UtcNow.AddDays(-1),retentionDays=7,scopedSince=DateTimeOffset.UtcNow.AddHours(-1),
                        requests=new{attempts=1,completed=1,accepted=0,failed=0,cancelled=0,p50Ms=3,p95Ms=10,latencySamples=1,sampleLimited=false},
                        ai=new[]{new{provider="Gemini",model="TEST-model",project="TEST-project",slot=1,attempts=1,succeeded=1,failed=0,interrupted=0,knownTokens=(long?)12,unknownTokens=0,cooldownKind=(string?)null,cooldownUntil=(DateTimeOffset?)null}},
                        errors=Array.Empty<object>(),openFeedback=1,resolvedFeedback=0,feedback=new[]{new{id=Guid.NewGuid(),owner=LastUser,conversationId=Guid.NewGuid(),messageId="12",kind="wrong_price",status="open",version=Guid.NewGuid().ToString("N"),updatedAt=DateTimeOffset.UtcNow}}}};
                    var bytes=JsonSerializer.SerializeToUtf8Bytes(reply);request.Response.ContentType="application/json";request.Response.ContentLength64=bytes.Length;await request.Response.OutputStream.WriteAsync(bytes);request.Response.Close();
                }catch{request.Response.StatusCode=500;request.Response.Close();}}}
        private static(JsonElement Header,JsonElement Body) Parts(string token){var p=token.Split('.');JsonElement Parse(string s)=>JsonDocument.Parse(Convert.FromBase64String(s.Replace('-','+').Replace('_','/')+new string('=',(4-s.Length%4)%4))).RootElement.Clone();return(Parse(p[0]),Parse(p[1]));}
        public void Dispose(){shutdown.Cancel();listener.Close();try{loop.GetAwaiter().GetResult();}catch{}shutdown.Dispose();}
    }
}
