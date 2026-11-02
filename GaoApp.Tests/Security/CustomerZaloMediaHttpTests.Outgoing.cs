using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GaoApp.Web.Services.CustomerService;
using Xunit;
namespace GaoApp.Tests.Security;
public sealed partial class CustomerZaloMediaHttpTests
{
    private static async Task CheckOutgoingHttp(HttpClient staff,HttpClient reader,HttpClient foreign,CustomerServiceStore store,CustomerStaff who,CustomerChannelConnection connection,Guid id,byte[] png)
    {
        var row=store.StaffGet(who,id);using(var claim=await staff.PostAsJsonAsync($"/admin/customer-service/conversations/{id}/commands",new{requestId=Guid.NewGuid(),version=row.Version,action="claim"}))Assert.Equal(HttpStatusCode.OK,claim.StatusCode);
        row=store.StaffGet(who,id);var key=Guid.NewGuid();var url=$"/admin/customer-service/conversations/{id}/media-replies";
        MultipartFormDataContent Form(Guid? requestId=null,long? version=null){var f=new MultipartFormDataContent();f.Add(new StringContent((requestId??key).ToString()),"RequestId");f.Add(new StringContent((version??row.Version).ToString()),"Version");f.Add(new StringContent("TEST HTTP caption"),"Caption");f.Add(new ByteArrayContent(png),"Asset","TEST.png");return f;}
        using(var f=Form())Assert.Equal(HttpStatusCode.Forbidden,(await reader.PostAsync(url,f)).StatusCode);
        using(var f=Form())Assert.Equal(HttpStatusCode.NotFound,(await foreign.PostAsync(url,f)).StatusCode);
        var csrf=staff.DefaultRequestHeaders.GetValues("RequestVerificationToken").Single();staff.DefaultRequestHeaders.Remove("RequestVerificationToken");
        using(var f=Form())Assert.Equal(HttpStatusCode.BadRequest,(await staff.PostAsync(url,f)).StatusCode);staff.DefaultRequestHeaders.Add("RequestVerificationToken",csrf);
        using(var f=Form()){using var accepted=await staff.PostAsync(url,f);Assert.True(accepted.StatusCode==HttpStatusCode.OK,await accepted.Content.ReadAsStringAsync());var body=await accepted.Content.ReadFromJsonAsync<JsonElement>();var message=body.GetProperty("messages").EnumerateArray().Single(m=>m.TryGetProperty("outgoing",out _));Assert.Equal("pending",message.GetProperty("delivery").GetProperty("state").GetString());Assert.False(message.GetProperty("outgoing").TryGetProperty("bytes",out _));}
        using(var f=Form())Assert.Equal(HttpStatusCode.OK,(await staff.PostAsync(url,f)).StatusCode);
        var sent=store.StaffGet(who,id);var outgoing=Assert.Single(sent.Messages,m=>m.Outgoing is not null);
        var preview=$"/admin/customer-service/conversations/{id}/messages/{outgoing.Id}/outgoing-media";
        using(var r=await reader.GetAsync(preview)){Assert.Equal(HttpStatusCode.OK,r.StatusCode);Assert.Equal(png,await r.Content.ReadAsByteArrayAsync());Assert.Equal("image/png",r.Content.Headers.ContentType!.MediaType);}
        using(var r=await foreign.GetAsync(preview))Assert.Equal(HttpStatusCode.NotFound,r.StatusCode);
        using(var r=await staff.GetAsync(preview+"?download=true"))Assert.Equal("attachment",r.Content.Headers.ContentDisposition!.DispositionType);
        // Prove this action overrides the controller's 24 KiB limit for real authenticated uploads.
        var large="%PDF-1.7 TEST ONLY"u8.ToArray().Concat(new byte[128*1024]).ToArray();var latest=store.StaffGet(who,id);
        using(var form=new MultipartFormDataContent()){
            form.Add(new StringContent(Guid.NewGuid().ToString()),"RequestId");form.Add(new StringContent(latest.Version.ToString()),"Version");form.Add(new ByteArrayContent(large),"Asset","TEST-large.pdf");
            using var response=await staff.PostAsync(url,form);Assert.True(response.StatusCode==HttpStatusCode.OK,await response.Content.ReadAsStringAsync());
            var added=store.StaffGet(who,id).Messages.Last(m=>m.Outgoing is not null);Assert.Equal(large,store.StaffOutgoingMedia(who,id,added.Id).Bytes);
        }
        store.PauseChannel(connection,true,store.ChannelState(connection).Version);var current=store.StaffGet(who,id);using(var f=Form(Guid.NewGuid(),current.Version))Assert.Equal(HttpStatusCode.Conflict,(await staff.PostAsync(url,f)).StatusCode);
        store.PauseChannel(connection,false,store.ChannelState(connection).Version);
    }
}
