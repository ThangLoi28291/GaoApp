using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Data.Sqlite;
using Xunit;
namespace GaoApp.Tests.Security;

public sealed class CustomerZaloMediaTests:IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"GaoZaloMedia",Guid.NewGuid().ToString("N"));
    private readonly AssistantIntegrationTests.FixedClock clock=new();
    private readonly AssistantIntegrationTests.Monitor<CustomerServiceOptions> options=new(new(){DatabasePath="media.db"});
    private readonly CustomerStaff staff=new(1,1,"TEST Nhân viên",true,true,true);
    private readonly Guid peer=Guid.NewGuid(),asset=Guid.NewGuid();
    private static readonly byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jBTkAAAAASUVORK5CYII=");
    public CustomerZaloMediaTests(){Directory.CreateDirectory(Path.Combine(directory,"credentials"));File.WriteAllText(Path.Combine(directory,"credentials","token"),"SYNTHETIC_BEARER_TOKEN_ONLY");}
    private CustomerChannelConnection Connection()=>new(){Id="zalo-media",Provider="zalomarketing",StoreId=1,AccountId=Guid.NewGuid().ToString("D"),Enabled=true,AutoReply=true,AccessTokenFile=Path.Combine(directory,"credentials","token"),BaseUrl="http://127.0.0.1:54321/"};
    private CustomerServiceStore Store()=>new(new AssistantIntegrationTests.EnvironmentStub(directory),options,clock);
    private CustomerChannelIncoming Input(string kind="Group")=>new(peer+":42",peer.ToString(),"TEST ảnh",clock.Now,"42",true,"TEST Nhóm",kind,new(42,"Image","TEST thành viên","TEST trích dẫn",[new(asset,"Image","test.png","image/png",png.Length)]));
    private Guid Import(CustomerServiceStore store,CustomerChannelConnection c,CustomerChannelIncoming? input=null){store.Configure(1,true,store.Settings(1).Version);return store.ImportChannel(c,input??Input())!.Value;}
    [Fact]public void Media_metadata_is_durable_scoped_to_store_message_asset_and_removed_on_expiry(){
        var c=Connection();var s=Store();var id=Import(s,c);var row=Store().StaffGet(staff,id);var message=Assert.Single(row.Messages,m=>m.Zalo is not null);
        Assert.Equal("Group",row.ConversationKind);Assert.Equal("TEST thành viên",message.Zalo!.SenderName);
        Assert.Equal(asset,Store().StaffZaloMedia(staff,id,message.Id,asset).Attachment.Id);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff with{StoreId=2},id,message.Id,asset)).Status);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff,id,Guid.NewGuid(),asset)).Status);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff,id,message.Id,Guid.NewGuid())).Status);
        clock.Now=clock.Now.AddDays(8);s.Purge();Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff,id,message.Id,asset)).Status);
        using var db=new SqliteConnection("Data Source="+Path.Combine(directory,"media.db"));db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM CskhChannelMessageSources";Assert.Equal(0L,(long)cmd.ExecuteScalar()!);
    }
    [Fact]public void Group_never_switches_to_AI_and_source_filters_distinguish_direct_group(){
        var c=Connection();var s=Store();var group=Import(s,c);var direct=Import(s,c,Input("Direct") with{ExternalId="direct:43",Peer=Guid.NewGuid().ToString(),Reference="43",Zalo=null,NeedsHuman=false});
        Assert.Equal(group,Assert.Single(s.Inbox(staff,"all",source:"zalo-group")).Id);Assert.Equal(direct,Assert.Single(s.Inbox(staff,"all",source:"zalo-direct")).Id);
        Assert.NotEqual(group,s.TakeChannelTurn(c)?.Who.ConversationId);var row=s.StaffGet(staff,group);row=s.StaffCommand(staff,group,new(Guid.NewGuid(),"claim",row.Version));
        Assert.Equal(409,Assert.Throws<AssistantException>(()=>s.StaffCommand(staff,group,new(Guid.NewGuid(),"bot",row.Version))).Status);
    }
    [Fact]public void Schema_four_upgrade_preserves_legacy_threads_and_adds_media_tables(){
        var c=Connection();var s=Store();var id=Import(s,c,Input("Direct") with{Zalo=null});
        using(var db=new SqliteConnection("Data Source="+Path.Combine(directory,"media.db"))){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE CskhChannelMessageSources;DROP TABLE CskhChannelThreadDetails;DROP TABLE CskhChannelHistoryMedia;PRAGMA user_version=4;";cmd.ExecuteNonQuery();}
        var restored=Store().StaffGet(staff,id);Assert.Equal("Direct",restored.ConversationKind);Assert.Contains(restored.Messages,m=>m.Text=="TEST ảnh");
        using var check=new SqliteConnection("Data Source="+Path.Combine(directory,"media.db"));check.Open();using var version=check.CreateCommand();version.CommandText="PRAGMA user_version";Assert.Equal(11L,(long)version.ExecuteScalar()!);
    }
    private CustomerChannelTransport Wire(CustomerChannelConnection c,Func<HttpRequestMessage,Task<HttpResponseMessage>> handler)=>new(new HttpClient(new CustomerChannelTransportTests.Handler(handler)),new(new AssistantIntegrationTests.Monitor<CustomerChannelOptions>(new(){CredentialDirectory=Path.Combine(directory,"credentials"),Connections=[c]}),new AssistantIntegrationTests.EnvironmentStub(Path.Combine(directory,"web"))),clock);
    private HttpResponseMessage Response(CustomerChannelConnection c,byte[] bytes,string type="image/png"){var r=new HttpResponseMessage(HttpStatusCode.OK){Content=new ByteArrayContent(bytes)};r.Content.Headers.ContentType=new(type);r.Headers.Add("X-ZaloMarketing-Sender-Profile-Id",c.AccountId);return r;}
    [Fact]public async Task Media_fetch_uses_only_fixed_origin_ids_and_bearer_then_validates_bytes(){
        var c=Connection();var calls=0;var target=new CustomerZaloMediaTarget(c.Id,c.Binding,peer.ToString(),42,Input().Zalo!.Attachments[0]);
        var wire=Wire(c,r=>{calls++;Assert.Equal(HttpMethod.Get,r.Method);Assert.Equal($"http://127.0.0.1:54321/api/v1/inbox/conversations/{peer:D}/messages/42/media/{asset:D}",r.RequestUri!.ToString());Assert.Equal("Bearer SYNTHETIC_BEARER_TOKEN_ONLY",r.Headers.Authorization!.ToString());return Task.FromResult(Response(c,png));});
        var result=await wire.ReadMedia(c,target,false,default);Assert.True(result.Preview);Assert.Equal(png,result.Bytes);Assert.Equal(1,calls);
    }
    [Theory][InlineData("account")][InlineData("size")][InlineData("type")][InlineData("signature")][InlineData("redirect")]
    public async Task Wrong_account_size_type_active_image_or_redirect_is_rejected(string problem){
        var c=Connection();var target=new CustomerZaloMediaTarget(c.Id,c.Binding,peer.ToString(),42,Input().Zalo!.Attachments[0]);
        var wire=Wire(c,r=>{var response=Response(c,problem=="signature"?Enumerable.Repeat((byte)'x',png.Length).ToArray():png);
            if(problem=="account"){response.Headers.Remove("X-ZaloMarketing-Sender-Profile-Id");response.Headers.Add("X-ZaloMarketing-Sender-Profile-Id",Guid.NewGuid().ToString());}
            if(problem=="size")response.Content.Headers.ContentLength=png.Length+1;
            if(problem=="type")response.Content.Headers.ContentType=new("text/html");
            if(problem=="redirect"){response.StatusCode=HttpStatusCode.Redirect;response.Headers.Location=new("https://outside.invalid/");}
            return Task.FromResult(response);
        });
        Assert.Equal(502,(await Assert.ThrowsAsync<AssistantException>(()=>wire.ReadMedia(c,target,false,default))).Status);
    }
    [Fact]public async Task Files_download_as_opaque_bytes_and_changed_binding_is_rejected_before_HTTP(){
        var c=Connection();var attachment=new CustomerZaloAttachment(asset,"File","../../evil.html","text/html",6);
        var target=new CustomerZaloMediaTarget(c.Id,c.Binding,peer.ToString(),42,attachment);var calls=0;
        var wire=Wire(c,r=>{calls++;return Task.FromResult(Response(c,"<html>"u8.ToArray(),"text/html"));});
        var result=await wire.ReadMedia(c,target,false,default);Assert.False(result.Preview);Assert.Equal("application/octet-stream",result.ContentType);Assert.Equal("evil.html",result.FileName);
        Assert.Equal(404,(await Assert.ThrowsAsync<AssistantException>(()=>wire.ReadMedia(c,target with{Binding="different"},false,default))).Status);Assert.Equal(1,calls);
    }
    [Fact]public void Legacy_media_appears_in_source_history_without_guessing_an_old_message_mapping(){
        var c=Connection();var s=Store();var input=Input("Direct") with{Text="[Khách gửi nội dung đính kèm; xem tại ZaloMarketing hoặc Zalo để hỗ trợ.]"};
        var id=Import(s,c,input with{Zalo=null});var before=s.StaffGet(staff,id);
        Assert.True(s.RefreshZaloMessage(c,input));var after=Store().StaffGet(staff,id);
        Assert.Equal(before.Messages.Length,after.Messages.Length);Assert.All(after.Messages,m=>Assert.Null(m.Zalo));
        var item=Assert.Single(after.ZaloHistory!);Assert.False(item.IsOutgoing);Assert.Equal(42,s.StaffZaloMedia(staff,id,item.Id,asset).Sequence);
        Assert.False(s.RefreshZaloMessage(c,input));Assert.Equal(after.Version,s.StaffGet(staff,id).Version);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff with{StoreId=2},id,item.Id,asset)).Status);
    }
    [Fact]public void Files_sent_directly_on_Zalo_are_read_only_history_and_expire_with_the_conversation(){
        var c=Connection();var s=Store();var id=Import(s,c);var before=s.StaffGet(staff,id);
        var source=Input() with{ExternalId="source-sent:43",Reference="43",Zalo=Input().Zalo! with{Sequence=43,SenderName="Bạn (Zalo)"}};
        Assert.True(s.SyncZaloHistory(c,source,true));var after=s.StaffGet(staff,id);
        Assert.Equal(before.Messages.Length,after.Messages.Length);var item=Assert.Single(after.ZaloHistory!);Assert.True(item.IsOutgoing);Assert.Null(s.TakeChannelDispatch(c));
        Assert.Equal(43,s.StaffZaloMedia(staff,id,item.Id,asset).Sequence);
        clock.Now=clock.Now.AddDays(8);s.Purge();Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffZaloMedia(staff,id,item.Id,asset)).Status);
    }
    [Fact]public void History_gallery_has_a_bounded_window_and_does_not_create_threads_for_seeded_receipts(){
        var c=Connection();var s=Store();s.Configure(1,true,0);Assert.False(s.SyncZaloHistory(c,Input(),true));Assert.Empty(s.Inbox(staff,"all"));
        var id=Import(s,c);
        for(var n=0;n<55;n++){clock.Now=clock.Now.AddSeconds(1);s.SyncZaloHistory(c,Input() with{ExternalId="sent-"+n,SentAt=clock.Now,Zalo=Input().Zalo! with{Sequence=100+n}},true);}
        Assert.Equal(50,s.StaffGet(staff,id).ZaloHistory!.Length);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(directory))Directory.Delete(directory,true);}
}
