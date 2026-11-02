using GaoApp.Web.Services.CustomerService;
using Xunit;
namespace GaoApp.Tests.Security;
public sealed partial class CustomerChannelRunnerTests
{
    [Fact]public async Task Group_history_members_attachments_and_staff_reply_keep_the_group_destination(){
        await using var f=new Fixture(true);f.ConversationKind="Group";f.MessageKind="Image";f.WithMedia=true;f.Fresh=true;
        await f.Runner.Run(f.Connection.Id,true,default);
        var row=Assert.Single(f.Store.Inbox(f.Agent,"all"));var thread=f.Store.StaffGet(f.Agent,row.Id);
        Assert.Equal("Group",thread.ConversationKind);Assert.Equal("waiting",thread.Mode);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
        var image=Assert.Single(thread.Messages,m=>m.Zalo?.Sequence==2);Assert.Equal(f.MediaId,Assert.Single(image.Zalo!.Attachments).Id);Assert.Contains("Thành viên",image.Zalo.SenderName);
        Assert.Equal(2,thread.Messages.Count(m=>m.Role=="customer"));
        f.Reply();await f.Runner.Run(f.Connection.Id,false,default);
        Assert.Single(f.Posts);Assert.Contains("\"replyToSequence\":2",f.Posts[0].Body);Assert.Equal(1,f.Sends);
    }
    [Fact]public async Task Direct_files_are_visible_and_do_not_invoke_AI(){
        await using var f=new Fixture(true);await f.Runner.Run(f.Connection.Id,true,default);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);f.Fresh=true;f.MessageKind="File";f.WithMedia=true;await f.Runner.Run(f.Connection.Id,true,default);
        var row=Assert.Single(f.Store.Inbox(f.Agent,"all"));var m=Assert.Single(f.Store.StaffGet(f.Agent,row.Id).Messages,x=>x.Role=="customer");
        Assert.Equal("Direct",row.ConversationKind);Assert.Equal("bao-gia.pdf",Assert.Single(m.Zalo!.Attachments).FileName);Assert.Equal("waiting",row.Mode);Assert.Equal(0,f.AiCalls);
    }
    [Fact]public async Task Delayed_attachment_updates_the_same_message_without_duplicate_import(){
        await using var f=new Fixture();await f.Runner.Run(f.Connection.Id,true,default);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);f.Fresh=true;f.MessageKind="Image";await f.Runner.Run(f.Connection.Id,true,default);
        var row=Assert.Single(f.Store.Inbox(f.Agent,"all"));var first=f.Store.StaffGet(f.Agent,row.Id);var id=Assert.Single(first.Messages,m=>m.Role=="customer").Id;
        f.WithMedia=true;await f.Runner.Run(f.Connection.Id,true,default);
        var next=f.Store.StaffGet(f.Agent,row.Id);Assert.Equal(first.Messages.Length,next.Messages.Length);
        Assert.Equal(id,Assert.Single(next.Messages,m=>m.Role=="customer").Id);Assert.Single(next.Messages.Single(m=>m.Id==id).Zalo!.Attachments);
        var version=next.Version;await f.Runner.Run(f.Connection.Id,true,default);Assert.Equal(version,f.Store.StaffGet(f.Agent,row.Id).Version);
    }
}
