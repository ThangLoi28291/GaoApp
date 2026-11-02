using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using GaoApp.Application.Common.Security;
using GaoApp.Web.Services.Assistant;
using Xunit;

namespace GaoApp.Tests.Security;

[Collection("SqlServerConcurrency")]
public sealed class AssistantPolicyHttpTests
{
    [Fact]
    public async Task Store_policy_admin_and_source_require_real_cookie_csrf_current_rights_and_store() {
        await using var app=await FullApplicationFixture.StartAsync();
        var rights=new[]{AssistantPermissions.Use,AssistantPermissions.Chat,PermissionCodes.System.Setting.Update};
        var account=await app.AddAccountAsync(app.Stores[0],rights);var otherAccount=await app.AddAccountAsync(app.Stores[1],rights);
        var employee=await app.AddAccountAsync(app.Stores[0],[AssistantPermissions.Use,AssistantPermissions.Chat]);
        using var own=await app.LoginAsync(account);using var other=await app.LoginAsync(otherAccount);using var staff=await app.LoginAsync(employee);
        using var rsa=RSA.Create(2048);var key=Path.Combine(Path.GetTempPath(),"policy-test-"+Guid.NewGuid().ToString("N")+".pem");await File.WriteAllTextAsync(key,rsa.ExportRSAPrivateKeyPem());
        try {
            await app.ConfigureAssistantAsync(new Dictionary<string,string>{["Enabled"]="true",["Issuer"]="https://policy.test",["KeyId"]="policy-test",["PrivateKeyFile"]=key});
            var options=new AssistantIntegrationTests.Monitor<AssistantOptions>(new(){Enabled=true,Issuer="https://policy.test",KeyId="policy-test",PrivateKeyFile=key});
            var jwt=new AssistantJwt(options,new AssistantIntegrationTests.EnvironmentStub(Path.GetTempPath()),TimeProvider.System);
            async Task<JsonElement> Lookup(int user,int store,string[] permissions) {
                var signed=jwt.Sign(new(user,"TEST",new(store.ToString(),null,null),permissions));
                using var req=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assistant/knowledge/lookup"){Content=JsonContent.Create(new{topics=new[]{"delivery","returns"}})};
                req.Headers.Authorization=new("Bearer",signed.Delegation);req.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());
                using var response=await own.Http.SendAsync(req); Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadFromJsonAsync<JsonElement>();
            }
            string Field(string html,string name)=>Regex.Match(html,"name=\""+name+"\" value=\"([^\"]+)\"").Groups[1].Value;
            var page=await own.Http.GetStringAsync("/admin/assistant/knowledge?topic=delivery");Assert.Contains("assistantKnowledgeAdmin",page);Assert.Contains("CẦN XÁC NHẬN",WebUtility.HtmlDecode(page));
            var csrf=own.Http.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();own.Http.DefaultRequestHeaders.Remove("RequestVerificationToken");
            using(var denied=await own.Http.PostAsync("/admin/assistant/knowledge/save",new FormUrlEncodedContent(new Dictionary<string,string>())))Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
            own.Http.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
            var values=new Dictionary<string,string>{["topic"]="delivery",["title"]="Giao hàng TEST",["body"]="TEST_ONLY_POLICY: Phí giao được nhân viên xác nhận theo khu vực. <script>alert('x')</script>",["from"]=DateTimeOffset.UtcNow.AddMinutes(-5).ToOffset(TimeSpan.FromHours(7)).ToString("yyyy-MM-dd'T'HH:mm:ss"),["sourceNote"]="Bộ kiểm thử tách biệt",["version"]=Field(page,"version"),["requestId"]=Guid.NewGuid().ToString()};
            using(var saved=await own.Http.PostAsync("/admin/assistant/knowledge/save",new FormUrlEncodedContent(values)))Assert.Equal(HttpStatusCode.Redirect,saved.StatusCode);
            var before=await Lookup(account.UserId,account.Store.StoreId,[AssistantPermissions.Use,AssistantPermissions.Chat]);Assert.Empty(before.GetProperty("articles").EnumerateArray());
            page=await own.Http.GetStringAsync("/admin/assistant/knowledge");Assert.DoesNotContain("<script>alert('x')</script>",page);
            var id=Regex.Match(page,"data-policy-id=\"([^\"]+)\"").Groups[1].Value;
            var review=new Dictionary<string,string>{["id"]=id,["decision"]="approve",["confirmed"]="true",["version"]=Field(page,"version"),["requestId"]=Guid.NewGuid().ToString()};
            using(var denied=await staff.Http.PostAsync("/admin/assistant/knowledge/review",new FormUrlEncodedContent(review)))Assert.True(denied.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Redirect);
            using(var approved=await own.Http.PostAsync("/admin/assistant/knowledge/review",new FormUrlEncodedContent(review)))Assert.Equal(HttpStatusCode.Redirect,approved.StatusCode);
            var otherPage=await other.Http.GetStringAsync("/admin/assistant/knowledge");
            var foreignReview=new Dictionary<string,string>(review){["version"]=Field(otherPage,"version"),["decision"]="withdraw",["requestId"]=Guid.NewGuid().ToString()};
            using(var foreign=await other.Http.PostAsync("/admin/assistant/knowledge/review",new FormUrlEncodedContent(foreignReview))) Assert.Equal(HttpStatusCode.Redirect,foreign.StatusCode);
            var read=await Lookup(account.UserId,account.Store.StoreId,[AssistantPermissions.Use,AssistantPermissions.Chat]);var article=Assert.Single(read.GetProperty("articles").EnumerateArray());Assert.Equal(Guid.Parse(id),article.GetProperty("revisionId").GetGuid());Assert.DoesNotContain("sourceNote",read.GetRawText());
            var isolated=await Lookup(otherAccount.UserId,otherAccount.Store.StoreId,[AssistantPermissions.Use,AssistantPermissions.Chat]);Assert.Empty(isolated.GetProperty("articles").EnumerateArray());
            Assert.DoesNotContain("TEST_ONLY_POLICY",await other.Http.GetStringAsync("/admin/assistant/knowledge"));
            var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","assistant-policy-step-b");Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"knowledge-page.html"),await own.Http.GetStringAsync("/admin/assistant/knowledge"));
            page=await own.Http.GetStringAsync("/admin/assistant/knowledge");review["version"]=Field(page,"version");review["decision"]="withdraw";review["requestId"]=Guid.NewGuid().ToString();
            using(var withdrawn=await own.Http.PostAsync("/admin/assistant/knowledge/review",new FormUrlEncodedContent(review)))Assert.Equal(HttpStatusCode.Redirect,withdrawn.StatusCode);
            Assert.Empty((await Lookup(account.UserId,account.Store.StoreId,[AssistantPermissions.Use,AssistantPermissions.Chat])).GetProperty("articles").EnumerateArray());
            var pair=jwt.Sign(new(employee.UserId,"TEST",new(account.Store.StoreId.ToString(),null,null),[AssistantPermissions.Use,AssistantPermissions.Chat]));
            using(var invalid=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assistant/knowledge/lookup"){Content=JsonContent.Create(new{topics=new[]{"delivery","delivery"}})}) {
                invalid.Headers.Authorization=new("Bearer",pair.Delegation);invalid.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());
                using var invalidResponse=await own.Http.SendAsync(invalid);Assert.Equal(HttpStatusCode.BadRequest,invalidResponse.StatusCode);
            }
            await using(var db=app.Database.CreateTenantContext(account.Store.StoreId)) {
                var permission=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Permissions,p=>p.Code==AssistantPermissions.Chat);
                var grants=await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(db.RolePermissions.Where(r=>r.RoleId==employee.RoleId&&r.PermissionId==permission.Id));db.RolePermissions.RemoveRange(grants);await db.SaveChangesAsync();
            }
            using(var revoked=new HttpRequestMessage(HttpMethod.Post,"/api/v1/assistant/knowledge/lookup"){Content=JsonContent.Create(new{topics=new[]{"delivery"}})}) {
                revoked.Headers.Authorization=new("Bearer",pair.Delegation);revoked.Headers.Add("X-Request-Id",Guid.NewGuid().ToString());
                using var denied=await own.Http.SendAsync(revoked);Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
            }
        }finally{File.Delete(key);}
    }
}