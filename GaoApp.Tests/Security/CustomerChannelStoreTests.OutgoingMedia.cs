using System.Security.Cryptography;
using GaoApp.Web.Services.Assistant;
using GaoApp.Web.Services.CustomerService;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Xunit;
namespace GaoApp.Tests.Security;
public sealed partial class CustomerChannelStoreTests
{
    private static readonly byte[] Png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jBTkAAAAASUVORK5CYII=");
    private static Task<CustomerOutgoingAsset> Asset(bool image=true)=>CustomerOutgoingMedia.Inspect(new FormFile(new MemoryStream(image?Png:"%PDF-1.7 TEST ONLY"u8.ToArray()),0,image?Png.Length:"%PDF-1.7 TEST ONLY"u8.Length,"Asset",image?"TEST ảnh.png":"TEST báo giá.pdf"),default);
    private CustomerChannelConnection ZaloMedia()=>new(){Id="zalo-media",Provider="zalomarketing",AccountId=Guid.NewGuid().ToString(),StoreId=1,Enabled=true};
    private (Guid Id,CustomerConversation Row) MediaThread(CustomerServiceStore s,CustomerChannelConnection c,string kind="Direct"){
        var peer=Guid.NewGuid().ToString();var id=Import(s,c,new("test-42",peer,"TEST incoming",clock.Now,"42",true,"TEST",kind));var row=s.StaffGet(agent,id);row=s.StaffCommand(agent,id,new(Guid.NewGuid(),"claim",row.Version));return(id,row);
    }
    [Theory][InlineData("Direct",true,"")][InlineData("Group",false,"TEST caption")]
    public async Task Media_bytes_caption_destination_and_reference_survive_restart_without_duplicate(string kind,bool image,string caption){
        var s=Store();var c=ZaloMedia();var(id,row)=MediaThread(s,c,kind);var asset=await Asset(image);var request=new CustomerMediaReply{RequestId=Guid.NewGuid(),Version=row.Version,Caption=caption};
        var sent=s.StaffMediaReply(agent,id,request,asset,c);var m=Assert.Single(sent.Messages,m=>m.Outgoing is not null);Assert.Equal(caption,m.Text);Assert.Equal("pending",m.Delivery!.State);Assert.Equal(asset.Bytes,Store().StaffOutgoingMedia(agent,id,m.Id).Bytes);
        var replay=Store().StaffMediaReply(agent,id,request,asset,c);Assert.Single(replay.Messages,m=>m.Outgoing is not null);
        Assert.Equal(409,Assert.Throws<AssistantException>(()=>Store().StaffMediaReply(agent,id,new(){RequestId=request.RequestId,Version=request.Version,Caption="CHANGED"},asset,c)).Status);
        var dispatch=Store().TakeChannelDispatch(c)!;Assert.Equal("42",dispatch.Reference);Assert.Equal(caption,dispatch.Text);Assert.Equal(asset.Bytes,dispatch.Media!.Bytes);Assert.Equal(asset.Sha256,dispatch.Media.Sha256);Assert.Equal(c.Binding,dispatch.Binding);
        s.FinishChannelDispatch(dispatch,new("accepted",JobId:Guid.NewGuid().ToString()));clock.Now=clock.Now.AddSeconds(16);var check=Store().TakeChannelDispatch(c)!;Assert.NotNull(check.JobId);Assert.Null(check.Media);
        Assert.Equal(404,Assert.Throws<AssistantException>(()=>s.StaffOutgoingMedia(agent with{StoreId=2},id,m.Id)).Status);
    }
    [Fact]public async Task Media_requires_owner_current_version_binding_rights_and_active_connection(){
        var s=Store();var c=ZaloMedia();var(id,row)=MediaThread(s,c);var a=await Asset();var input=new CustomerMediaReply{RequestId=Guid.NewGuid(),Version=row.Version};
        Assert.Equal(403,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent with{CanReply=false},id,input,a,c)).Status);
        Assert.Equal(409,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent with{UserId=99},id,input,a,c)).Status);
        Assert.Equal(409,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent,id,new(){RequestId=input.RequestId,Version=row.Version-1},a,c)).Status);
        var other=ZaloMedia();Assert.Equal(409,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent,id,input,a,other)).Status);
        s.PauseChannel(c,true,s.ChannelState(c).Version);Assert.Equal(409,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent,id,input,a,c)).Status);Assert.DoesNotContain(s.StaffGet(agent,id).Messages,m=>m.Outgoing is not null);
    }
    [Fact]public async Task Rejected_quota_and_tampered_bytes_leave_no_message_request_or_outbox_and_retention_cascades(){
        var s=Store();var c=ZaloMedia();var(id,row)=MediaThread(s,c);var a=await Asset();var input=new CustomerMediaReply{RequestId=Guid.NewGuid(),Version=row.Version};
        Assert.Equal(422,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent,id,input,a with{Sha256=new string('0',64)},c)).Status);
        options.CurrentValue.MaximumOutgoingMediaBytesPerStore=1;Assert.Equal(429,Assert.Throws<AssistantException>(()=>s.StaffMediaReply(agent,id,input,a,c)).Status);
        Assert.Null(s.TakeChannelDispatch(c));Assert.DoesNotContain(s.StaffGet(agent,id).Messages,m=>m.Outgoing is not null);
        options.CurrentValue.MaximumOutgoingMediaBytesPerStore=200*1024*1024;Assert.Single(s.StaffMediaReply(agent,id,input,a,c).Messages,m=>m.Outgoing is not null);
        clock.Now=clock.Now.AddDays(8);s.Purge();using var db=new SqliteConnection("Data Source="+Path.Combine(directory,"channels.db")+";Pooling=False");db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM CskhOutgoingAssets";Assert.Equal(0L,cmd.ExecuteScalar());
    }
    [Theory][InlineData("attack.png","<script>bad</script>")][InlineData("script.svg","<svg/>")][InlineData("../bad.pdf","%PDF-1.7")][InlineData("bad.xlsx","NOT ZIP")][InlineData("empty.pdf","")]
    public async Task Upload_checks_actual_format_name_and_empty_bytes(string name,string content){var b=System.Text.Encoding.UTF8.GetBytes(content);await Assert.ThrowsAsync<AssistantException>(()=>CustomerOutgoingMedia.Inspect(new FormFile(new MemoryStream(b),0,b.Length,"Asset",name),default));}
}
