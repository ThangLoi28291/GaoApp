using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
namespace GaoApp.Tests.Security;

public sealed partial class CustomerZaloMediaHttpTests
{
    [Fact]public async Task Real_login_store_permission_media_proxy_and_group_inbox_work_without_real_Zalo()
    {
        var files=Path.Combine(Path.GetTempPath(),"GaoZaloMediaHttp",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(files);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{EnvironmentName="Development",ContentRootPath=files});builder.WebHost.UseUrls("http://127.0.0.1:0");builder.Logging.ClearProviders();
        await using var source=builder.Build();var profile=Guid.NewGuid();var peer=Guid.NewGuid();var image=Guid.NewGuid();var file=Guid.NewGuid();var requests=0;var wrongProfile=false;
        var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jBTkAAAAASUVORK5CYII=");
        source.MapGet("/api/v1/inbox/conversations/{conversation:guid}/messages/{sequence:long}/media/{asset:guid}",async context=>{
            requests++;Assert.Equal("Bearer SYNTHETIC_ZALO_TOKEN_ONLY",context.Request.Headers.Authorization.ToString());
            Assert.Equal(peer.ToString("D"),context.Request.RouteValues["conversation"]!.ToString());Assert.Contains(context.Request.RouteValues["sequence"]!.ToString(),new[]{"42","43"});
            context.Response.Headers["X-ZaloMarketing-Sender-Profile-Id"]=(wrongProfile?Guid.NewGuid():profile).ToString("D");
            var isImage=context.Request.RouteValues["asset"]!.ToString()==image.ToString("D");var content=isImage?png:"%PDF-1.7 TEST ONLY"u8.ToArray();
            context.Response.ContentType=isImage?"image/png":"application/pdf";context.Response.ContentLength=content.Length;await context.Response.Body.WriteAsync(content);
        });
        await source.StartAsync();var address=source.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        try {
            File.WriteAllText(Path.Combine(files,"token"),"SYNTHETIC_ZALO_TOKEN_ONLY");
            await using var app=await FullApplicationFixture.StartAsync();
            var account=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View,CustomerServiceRights.Reply,CustomerServiceRights.Manage);
            var reader=await app.AddAccountAsync(app.Stores[0],CustomerServiceRights.View);
            var foreign=await app.AddAccountAsync(app.Stores[1],CustomerServiceRights.View,CustomerServiceRights.Reply);
            var denied=await app.AddAccountAsync(app.Stores[0],"catalog.product.view");
            await app.ConfigureCustomerChannelsAsync(new Dictionary<string,string>{
                ["Connections__0__Id"]="zalo-media-test",["Connections__0__StoreId"]=account.Store.StoreId.ToString(),["Connections__0__Provider"]="zalomarketing",
                ["Connections__0__AccountId"]=profile.ToString(),["Connections__0__Enabled"]="true",["Connections__0__AutoReply"]="false",["Connections__0__BaseUrl"]=address+"/",["Connections__0__AccessTokenFile"]=Path.Combine(files,"token")});
            using var staff=await app.LoginAsync(account);using var read=await app.LoginAsync(reader);using var wrong=await app.LoginAsync(foreign);using var noRight=await app.LoginAsync(denied);
            var store=new CustomerServiceStore(new AssistantIntegrationTests.EnvironmentStub(files),new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new(){DatabasePath=app.CustomerServiceDatabasePath}),TimeProvider.System);
            store.Configure(account.Store.StoreId,true,0);
            var connection=new CustomerChannelConnection{Id="zalo-media-test",StoreId=account.Store.StoreId,Provider="zalomarketing",AccountId=profile.ToString(),Enabled=true,BaseUrl=address+"/",AccessTokenFile=Path.Combine(files,"token")};
            var metadata=new CustomerZaloMessage(42,"Image","TEST Thành viên <img src=x onerror=alert(1)>","TEST trích dẫn",[
                new(image,"Image","TEST ảnh.png","image/png",png.Length),new(file,"File","TEST báo giá.pdf","application/pdf","%PDF-1.7 TEST ONLY"u8.Length)]);
            var id=store.ImportChannel(connection,new(peer+":42",peer.ToString(),"TEST ảnh và báo giá",DateTimeOffset.UtcNow,"42",true,"TEST Nhóm CSKH","Group",metadata))!.Value;
            var who=new CustomerStaff(account.UserId,account.Store.StoreId,"TEST",true,true);var row=store.StaffGet(who,id);var message=Assert.Single(row.Messages,m=>m.Zalo is not null);
            store.SyncZaloHistory(connection,new(peer+":43",peer.ToString(),"TEST tệp gửi trực tiếp",DateTimeOffset.UtcNow,"43",true,"TEST Nhóm CSKH","Group",metadata with{Sequence=43,SenderName="Bạn (Zalo)"}),true);
            var history=Assert.Single(store.StaffGet(who,id).ZaloHistory!);
            using(var response=await read.Http.GetAsync($"/admin/customer-service/conversations/{id}/messages/{history.Id}/media/{image}"))Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            var imagePath=$"/admin/customer-service/conversations/{id}/messages/{message.Id}/media/{image}";
            using(var response=await read.Http.GetAsync(imagePath)){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal(png,await response.Content.ReadAsByteArrayAsync());Assert.Equal("image/png",response.Content.Headers.ContentType!.MediaType);Assert.Contains("no-store",response.Headers.CacheControl!.ToString());Assert.Equal("nosniff",response.Headers.GetValues("X-Content-Type-Options").Single());}
            using(var response=await staff.Http.GetAsync($"/admin/customer-service/conversations/{id}/messages/{message.Id}/media/{file}?download=true")){Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Equal("application/octet-stream",response.Content.Headers.ContentType!.MediaType);Assert.Equal("attachment",response.Content.Headers.ContentDisposition!.DispositionType);}
            var before=requests;
            using(var response=await wrong.Http.GetAsync(imagePath))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
            using(var response=await noRight.Http.GetAsync(imagePath))Assert.Equal(HttpStatusCode.Forbidden,response.StatusCode);
            using(var response=await staff.Http.GetAsync(imagePath.Replace(image.ToString(),Guid.NewGuid().ToString())))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
            using(var response=await staff.Http.GetAsync(imagePath.Replace(message.Id.ToString(),Guid.NewGuid().ToString())))Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
            Assert.Equal(before,requests);
            wrongProfile=true;using(var response=await staff.Http.GetAsync(imagePath))Assert.Equal(HttpStatusCode.BadGateway,response.StatusCode);wrongProfile=false;
            var list=await staff.Http.GetFromJsonAsync<JsonElement>("/admin/customer-service/conversations?mode=all&source=zalo-group");Assert.Single(list.GetProperty("items").EnumerateArray());Assert.Equal("Group",list.GetProperty("items")[0].GetProperty("conversationKind").GetString());
            var detail=await staff.Http.GetFromJsonAsync<JsonElement>($"/admin/customer-service/conversations/{id}");Assert.Equal("Group",detail.GetProperty("conversationKind").GetString());
            Assert.Equal(2,detail.GetProperty("messages").EnumerateArray().Single(m=>m.TryGetProperty("zalo",out _)).GetProperty("zalo").GetProperty("attachments").GetArrayLength());
            var output=Path.Combine(FullApplicationFixture.SourceRoot(),"Logs","customer-service-zalo-media-groups");Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output,"inbox-page.html"),await staff.Http.GetStringAsync("/admin/customer-service"));
            await File.WriteAllTextAsync(Path.Combine(output,"conversation.example.json"),detail.GetRawText());
            await CheckOutgoingHttp(staff.Http,read.Http,wrong.Http,store,who,connection,id,png);
            store.PauseChannel(connection,true,store.ChannelState(connection).Version);before=requests;
            using(var response=await staff.Http.GetAsync(imagePath))Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.Equal(before,requests);
        } finally {await source.StopAsync();if(Directory.Exists(files))Directory.Delete(files,true);}
    }
}
