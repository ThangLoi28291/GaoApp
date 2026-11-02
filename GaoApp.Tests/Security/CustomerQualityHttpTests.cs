using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.CustomerService;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace GaoApp.Tests.Security;
[Collection("SqlServerConcurrency")]
public sealed class CustomerQualityHttpTests
{
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\""+name+"\"[^>]*value=\"([^\"]*)\"").Groups[1].Value);
    private static async Task<JsonElement> Post(HttpClient client,string path,object body){using var response=await client.PostAsJsonAsync(path,body);var raw=await response.Content.ReadAsStringAsync();Assert.True(response.IsSuccessStatusCode,$"{path}: {(int)response.StatusCode} {raw}");return JsonDocument.Parse(raw).RootElement.Clone();}
    [Fact]public async Task Real_cookie_csrf_feedback_review_catalog_learning_evaluation_and_restart_are_scoped_and_durable(){
        await using var app=await FullApplicationFixture.StartAsync();
        // This fixture deliberately executes many retries in one burst; production budgets remain unchanged.
        await app.ConfigureResourceProtectionAsync(new Dictionary<string,string>{["Limits__AssistantWork__CallerPerWindow"]="200",["Limits__AssistantWork__StorePerWindow"]="600",["Limits__AssistantWork__ProcessPerWindow"]="2400"});
        var rights=new[]{CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage,AssistantPermissions.Use,AssistantPermissions.Chat,PermissionCodes.Catalog.Product.View,PermissionCodes.System.Setting.Update};
        var account=await app.AddAccountAsync(app.Stores[0],rights);var foreign=await app.AddAccountAsync(app.Stores[1],rights);var reader=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View);var managerOnly=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View,CustomerServiceRights.Manage);
        using var rsa=RSA.Create(2048);var key=Path.Combine(Path.GetTempPath(),"GaoQualityHttp-"+Guid.NewGuid().ToString("N")+".pem");await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());
        try{
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{["Enabled"]="true",["Issuer"]="https://quality.test",["KeyId"]="quality-test",["PrivateKeyFile"]=key,["BaseUrl"]="http://127.0.0.1:9/"});
            using var own=await app.LoginAsync(account);using var other=await app.LoginAsync(foreign);using var read=await app.LoginAsync(reader);using var limited=await app.LoginAsync(managerOnly);
            var inbox=await own.Http.GetStringAsync("/admin/customer-service");using(var enabled=await own.Http.PostAsync("/admin/customer-service/settings",new FormUrlEncodedContent(new Dictionary<string,string>{["enabled"]="true",["version"]=Field(inbox,"version")})))Assert.Equal(HttpStatusCode.Redirect,enabled.StatusCode);
            using var guest=app.Anonymous(account.Store);using var guest2=app.Anonymous(account.Store);var support=await guest.Http.GetStringAsync("/support");guest.Http.DefaultRequestHeaders.Add("RequestVerificationToken",Field(support,"__RequestVerificationToken"));var support2=await guest2.Http.GetStringAsync("/support");guest2.Http.DefaultRequestHeaders.Add("RequestVerificationToken",Field(support2,"__RequestVerificationToken"));
            var c=await Post(guest.Http,"/support/conversations",new{requestId=Guid.NewGuid(),name="PRIVATE CUSTOMER NAME",contact="PRIVATE CONTACT",consent=true});var conversation=c.GetProperty("id").GetGuid();
            c=await Post(guest.Http,$"/support/conversations/{conversation}/messages",new{requestId=Guid.NewGuid(),message="đơn hàng của tôi"});var message=c.GetProperty("messages").EnumerateArray().Last(m=>m.GetProperty("role").GetString()=="assistant").GetProperty("id").GetGuid();var ratePath=$"/support/conversations/{conversation}/feedback";
            var rating=new{requestId=Guid.NewGuid(),messageId=message,kind="wrong_product"};var csrf=guest.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();guest.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");using(var denied=await guest.Http.PostAsJsonAsync(ratePath,rating))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);guest.Http.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
            await Post(guest.Http,ratePath,rating);await Post(guest.Http,ratePath,rating);using(var denied=await guest2.Http.GetAsync(ratePath))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            using(var denied=await read.Http.PostAsJsonAsync($"/admin/customer-service/conversations/{conversation}/feedback",rating))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            await Post(own.Http,$"/admin/customer-service/conversations/{conversation}/feedback",new{requestId=Guid.NewGuid(),messageId=message,kind="unclear"});var guestVotes=await guest.Http.GetFromJsonAsync<JsonElement>(ratePath);Assert.Equal("wrong_product",Assert.Single(guestVotes.GetProperty("items").EnumerateArray()).GetProperty("kind").GetString());
            const string quality="/admin/customer-service/quality";using(var denied=await read.Http.GetAsync(quality))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);using(var denied=await guest.Http.GetAsync(quality+"/report"))Assert.True(denied.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Redirect);
            var report=await own.Http.GetFromJsonAsync<JsonElement>(quality+"/report?storeId="+foreign.Store.StoreId);Assert.Equal(account.Store.StoreId,report.GetProperty("storeId").GetInt32());Assert.Equal(1,report.GetProperty("report").GetProperty("customerVotes").GetInt32());Assert.Equal(1,report.GetProperty("report").GetProperty("staffVotes").GetInt32());
            var queue=await own.Http.GetFromJsonAsync<JsonElement>(quality+"/feedback");var row=queue.GetProperty("queue").GetProperty("items").EnumerateArray().Single(i=>i.GetProperty("actorType").GetString()=="customer");var feedback=row.GetProperty("id").GetGuid();var version=row.GetProperty("version").GetString();
            using(var denied=await other.Http.PostAsJsonAsync(quality+$"/feedback/{feedback}/review",new{requestId=Guid.NewGuid(),version,action="resolve",resolution="not_issue"}))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            var review=new{requestId=Guid.NewGuid(),version,action="resolve",resolution="not_issue"};await Post(own.Http,quality+$"/feedback/{feedback}/review",review);await Post(own.Http,quality+$"/feedback/{feedback}/review",review);
            string sku;await using(var db=app.Database.CreateTenantContext(account.Store.StoreId))sku=await db.ProductVariants.Where(v=>v.Id==account.Store.VariantId).Select(v=>v.Sku).SingleAsync();
            object Case(int variant,Guid request)=>new{requestId=request,id=Guid.Empty,version=(string?)null,query=sku,kind="exact",expectedVariantIds=new[]{variant},split="holdout",state="active",independent=true,sanitized=true};
            using(var denied=await own.Http.PostAsJsonAsync(quality+"/cases",Case(foreign.Store.VariantId,Guid.NewGuid())))Assert.Equal(HttpStatusCode.NotFound,denied.StatusCode);
            using(var denied=await limited.Http.PostAsJsonAsync(quality+"/cases",Case(account.Store.VariantId,Guid.NewGuid())))Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            var casePayload=Case(account.Store.VariantId,Guid.NewGuid());var saved=await Post(own.Http,quality+"/cases",casePayload);var retry=await Post(own.Http,quality+"/cases",casePayload);Assert.Equal(saved.GetProperty("id").GetGuid(),retry.GetProperty("id").GetGuid());
            var suite=await own.Http.GetFromJsonAsync<JsonElement>(quality+"/cases");Assert.Single(suite.GetProperty("items").EnumerateArray());var run=new{requestId=Guid.NewGuid(),suiteVersion=suite.GetProperty("suiteVersion").GetString()};var evaluation=await Post(own.Http,quality+"/evaluations",run);Assert.Equal("insufficient_holdout",evaluation.GetProperty("report").GetProperty("gate").GetString());var again=await Post(own.Http,quality+"/evaluations",run);Assert.Equal(evaluation.GetProperty("report").GetProperty("id").GetGuid(),again.GetProperty("report").GetProperty("id").GetGuid());
            const string phrase="cách gọi đã biên soạn TEST";var proposal=new{requestId=Guid.NewGuid(),query=phrase,variantId=account.Store.VariantId,sanitized=true};var proposed=await Post(own.Http,quality+$"/feedback/{feedback}/propose",proposal);Assert.Equal("pending",proposed.GetProperty("status").GetString());var proposedAgain=await Post(own.Http,quality+$"/feedback/{feedback}/propose",proposal);Assert.Equal(proposed.GetProperty("id").GetGuid(),proposedAgain.GetProperty("id").GetGuid());
            var before=await own.Http.GetFromJsonAsync<JsonElement>("/admin/api/assistant/search/products?query="+Uri.EscapeDataString(phrase));Assert.Empty(before.GetProperty("items").EnumerateArray());
            using(var bad=await own.Http.PostAsJsonAsync(quality+"/cases",new{requestId=Guid.NewGuid(),id=Guid.Empty,version=(string?)null,query=phrase,kind="exact",expectedVariantIds=new[]{account.Store.VariantId},split="holdout",state="active",independent=true,sanitized=true}))Assert.Equal((HttpStatusCode)422,bad.StatusCode);
            var exported=await own.Http.GetStringAsync(quality+"/cases.json");Assert.DoesNotContain("PRIVATE CUSTOMER",exported);Assert.DoesNotContain("PRIVATE CONTACT",exported);Assert.DoesNotContain("đơn hàng của tôi",exported);Assert.DoesNotContain("reply",exported,StringComparison.OrdinalIgnoreCase);
            using(var duplicate=await own.Http.PostAsync(quality+"/evaluations",new StringContent("{\"requestId\":\""+Guid.NewGuid()+"\",\"requestId\":\""+Guid.NewGuid()+"\",\"suiteVersion\":\"x\"}",System.Text.Encoding.UTF8,"application/json")))Assert.Equal(HttpStatusCode.BadRequest,duplicate.StatusCode);
            var html=await own.Http.GetStringAsync(quality);var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","customer-service-step-f");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"quality-page.html"),html);await File.WriteAllTextAsync(Path.Combine(output,"support-page.html"),support);await File.WriteAllTextAsync(Path.Combine(output,"inbox-page.html"),inbox);
            await app.RestartAsync(enableDeliveryOutbox:false);Assert.Single((await guest.Http.GetFromJsonAsync<JsonElement>(ratePath)).GetProperty("items").EnumerateArray());Assert.Single((await own.Http.GetFromJsonAsync<JsonElement>(quality+"/evaluations")).GetProperty("items").EnumerateArray());
            await using(var db=app.Database.CreateTenantContext(account.Store.StoreId)){var permission=await db.Permissions.SingleAsync(p=>p.Code==CustomerServiceRights.Manage);var grant=await db.RolePermissions.SingleAsync(r=>r.RoleId==account.RoleId&&r.PermissionId==permission.Id);db.RolePermissions.Remove(grant);await db.SaveChangesAsync();}
            using(var revoked=await own.Http.GetAsync(quality+"/report"))Assert.Equal(HttpStatusCode.Forbidden,revoked.StatusCode);
        }finally{File.Delete(key);}
    }
}
