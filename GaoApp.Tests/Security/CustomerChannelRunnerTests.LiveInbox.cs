using GaoApp.Web.Services.CustomerService;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class CustomerChannelRunnerTests
{
    [Fact]
    public async Task Waiting_source_imports_retained_text_for_staff_without_AI_and_staff_reply_reconciles()
    {
        await using var f=new Fixture(true);
        var source=new PagedInbox(f){SourceStatus="Waiting"};
        source.Baseline();
        f.InboxSource=source.Read;
        await f.Runner.Run(f.Connection.Id,true,default);
        var conversation=Assert.Single(f.Store.Inbox(f.Agent,"all"));
        Assert.Equal("waiting",conversation.Mode);
        Assert.Single(Imported(f));
        var state=f.Store.ChannelState(f.Connection);
        Assert.True(state.SyncIncomplete);
        Assert.Equal("zalo_source_history_incomplete",state.Error);
        Assert.Null(f.Store.TakeChannelTurn(f.Connection));
        Assert.Null(f.Store.TakeChannelPrivateTurn(f.Connection));
        Assert.Equal(0,f.AiCalls);
        Assert.Equal(0,f.Sends);

        f.InboxSource=null;
        var id=f.Reply();
        await f.Runner.Run(f.Connection.Id,false,default);
        Assert.Equal(1,f.Sends);
        Assert.Equal("accepted",f.Store.StaffGet(f.Agent,id).Messages.Single(m=>m.Role=="agent").Delivery!.State);
        f.Clock.Now=f.Clock.Now.AddSeconds(16);
        await f.Runner.Run(f.Connection.Id,false,default);
        Assert.Equal("sent",f.Store.StaffGet(f.Agent,id).Messages.Single(m=>m.Role=="agent").Delivery!.State);
        Assert.Equal(1,f.Sends);
        Assert.Equal(0,f.AiCalls);
    }

    [Fact]
    public async Task Waiting_source_reads_all_retained_pages_before_activation_without_enabling_AI()
    {
        await using var f=new Fixture(true);
        var source=new PagedInbox(f){SourceStatus="Waiting"};source.Baseline();
        for(var i=2;i<=61;i++)source.Messages[f.Peer].Add(source.Frame(i,"TEST retained "+i,f.Clock.Now.AddMinutes(-5)));
        f.InboxSource=source.Read;
        await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Equal(61,Imported(f).Length);
        Assert.Contains(source.Details,p=>p.Before is not null);
        Assert.Equal("zalo_source_history_incomplete",f.Store.ChannelState(f.Connection).Error);
        Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Theory]
    [InlineData("Waiting",false)]
    [InlineData("Paused",true)]
    [InlineData("LoginRequired",true)]
    [InlineData("Error",true)]
    [InlineData("Unknown",true)]
    public async Task Unavailable_sources_still_block_import(string status,bool enabled)
    {
        await using var f=new Fixture(true);
        var source=new PagedInbox(f){SourceStatus=status,SourceEnabled=enabled};
        source.Baseline();f.InboxSource=source.Read;
        await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Empty(f.Store.Inbox(f.Agent,"all"));
        Assert.Equal("zalo_source_not_ready",f.Store.ChannelState(f.Connection).Error);
        Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task Resumed_batch_rechecks_readiness_and_does_not_import_after_source_pauses()
    {
        await using var f=new Fixture(true);
        var source=new PagedInbox(f,peers:10);source.Baseline();source.Add(1);
        f.InboxSource=source.Read;
        await f.Runner.Run(f.Connection.Id,true,default);
        var before=Imported(f).Length;
        source.SourceStatus="Paused";
        await f.MakeRunner(f.Store).Run(f.Connection.Id,true,default);
        Assert.Equal(before,Imported(f).Length);
        Assert.Equal("zalo_source_not_ready",f.Store.ChannelState(f.Connection).Error);
        Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }
}
