using System.Net;
using GaoApp.Web.Services.CustomerService;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class CustomerChannelRunnerTests
{
    private sealed class LivePilotInbox
    {
        private readonly Fixture f;
        internal string Status="Waiting",ProofTransport="Sdk",ProofPeer="1234567890",ProofKind="Direct";
        internal bool Enabled=true,Stale;
        internal Guid? ProofProfile;
        internal LivePilotInbox(Fixture fixture)
        {
            f=fixture;
            f.Connection.LiveReplyPilot=new(){Enabled=true,ConversationId=f.Peer,NativePeer=ProofPeer,AfterIncomingSequence=1,StartedAtUtc=f.Clock.Now};
            f.InboxSource=Read;
        }
        private HttpResponseMessage Read(HttpRequestMessage request)
        {
            if(request.RequestUri!.AbsolutePath=="/api/v1/inbox/conversations")
                return CustomerChannelTransportTests.Json(new{sync=new{enabled=Enabled,status=Status,lastAttemptAtUtc=Stale?f.Clock.Now.AddMinutes(-2):f.Clock.Now},conversations=Array.Empty<object>(),hasNextPage=false});
            var observed=f.Fresh?f.Clock.Now:f.Clock.Now.AddHours(-1);var seq=f.Fresh?2:1;
            return CustomerChannelTransportTests.Json(new{id=f.Peer,kind="Direct",displayName="TEST pilot",hasOlder=false,
                messages=new[]{new{sequence=seq,text="TEST sữa tươi",kind=f.MessageKind,observedAtUtc=observed,isOutgoing=false,senderId="1234567890",attachments=Array.Empty<object>()}},
                recipientEvidence=new{transport=ProofTransport,kind=ProofKind,senderProfileId=ProofProfile??Guid.Parse(f.Connection.AccountId),conversationId=f.Peer,
                    nativePeer=ProofPeer,latestIncomingSequence=seq,observedAtUtc=observed}});
        }
    }

    [Fact]
    public async Task Live_pilot_replies_to_new_text_and_reconciles_without_replaying_baseline()
    {
        await using var f=new Fixture(true);_ =new LivePilotInbox(f);
        await f.Receive();
        Assert.Equal(1,f.AiCalls);Assert.Equal(1,f.Sends);
        Assert.Equal("zalo_source_history_incomplete",f.Store.ChannelState(f.Connection).Error);
        f.Clock.Now=f.Clock.Now.AddSeconds(16);await f.Runner.Run(f.Connection.Id,false,default);
        var row=Assert.Single(f.Store.Inbox(f.Agent,"all"));
        Assert.Equal("sent",f.Store.StaffGet(f.Agent,row.Id).Messages.Single(m=>m.Role=="assistant"&&m.Delivery is not null).Delivery!.State);
        await f.Runner.Run(f.Connection.Id,true,default);Assert.Equal(1,f.AiCalls);Assert.Equal(1,f.Sends);
    }

    [Theory]
    [InlineData("peer")][InlineData("profile")][InlineData("transport")][InlineData("group")][InlineData("paused")][InlineData("disabled")][InlineData("stale")]
    public async Task Live_pilot_rejects_untrusted_or_unavailable_source(string failure)
    {
        await using var f=new Fixture(true);var source=new LivePilotInbox(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);f.Fresh=true;
        switch(failure){case "peer":source.ProofPeer="9876543210";break;case "profile":source.ProofProfile=Guid.NewGuid();break;case "transport":source.ProofTransport="Browser";break;
            case "group":source.ProofKind="Group";break;case "paused":source.Status="Paused";break;case "disabled":source.Enabled=false;break;case "stale":source.Stale=true;break;}
        await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Empty(f.Store.Inbox(f.Agent,"all"));Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task Live_pilot_never_takes_another_peer_even_if_it_already_has_a_pending_AI_turn()
    {
        await using var f=new Fixture(true);_=new LivePilotInbox(f);await f.Runner.Run(f.Connection.Id,true,default);
        f.Clock.Now=f.Clock.Now.AddSeconds(1);
        f.Store.ImportChannel(f.Connection,new("TEST other-peer",Guid.NewGuid().ToString("D"),"TEST sữa tươi",f.Clock.Now,"2"));
        await f.Runner.Run(f.Connection.Id,false,default);
        Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task Live_pilot_releases_only_the_initial_unclaimed_history_waiting_state_once()
    {
        await using var f=new Fixture(true);_=new LivePilotInbox(f);
        var id=f.Store.ImportChannel(f.Connection,new("TEST historical",f.Peer.ToString("D"),"TEST old text",f.Clock.Now.AddMinutes(-1),"1",true))!.Value;
        await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Equal("bot",f.Store.StaffGet(f.Agent,id).Mode);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);f.Fresh=true;await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Equal(1,f.AiCalls);Assert.Equal(1,f.Sends);
        f.Store.ImportChannel(f.Connection,new("TEST later handoff",f.Peer.ToString("D"),"TEST need staff",f.Clock.Now,"3",true));
        f.Store.PrepareZaloLivePilot(f.Connection);
        Assert.Equal("waiting",f.Store.StaffGet(f.Agent,id).Mode);
    }

    [Fact]
    public async Task Live_pilot_does_not_take_over_a_human_claim()
    {
        await using var f=new Fixture(true);_=new LivePilotInbox(f);
        var id=f.Store.ImportChannel(f.Connection,new("TEST historical",f.Peer.ToString("D"),"TEST old text",f.Clock.Now.AddMinutes(-1),"1",true))!.Value;
        var row=f.Store.StaffGet(f.Agent,id);f.Store.StaffCommand(f.Agent,id,new(Guid.NewGuid(),"claim",row.Version));
        f.Clock.Now=f.Clock.Now.AddMinutes(1);f.Fresh=true;await f.Runner.Run(f.Connection.Id,true,default);
        Assert.Equal("human",f.Store.StaffGet(f.Agent,id).Mode);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task Live_pilot_stops_dispatch_when_disabled_while_AI_is_generating()
    {
        await using var f=new Fixture(true);_=new LivePilotInbox(f);f.AfterAiResponse=()=>{f.Connection.LiveReplyPilot.Enabled=false;return Task.CompletedTask;};
        await f.Receive();Assert.Equal(1,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public async Task Live_pilot_expired_readiness_stops_pending_AI_work()
    {
        await using var f=new Fixture(true);_=new LivePilotInbox(f);await f.Runner.Run(f.Connection.Id,true,default);
        f.Clock.Now=f.Clock.Now.AddMinutes(2);
        f.Store.ImportChannel(f.Connection,new("TEST expired",f.Peer.ToString("D"),"TEST sữa tươi",f.Clock.Now,"2"));
        await f.Runner.Run(f.Connection.Id,false,default);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }

    [Fact]
    public void Live_pilot_configuration_cannot_expand_to_another_phone()
    {
        var c=new CustomerChannelConnection{Provider="zalomarketing",Enabled=true,AutoReply=true};
        c.LiveReplyPilot=new(){Enabled=true,ConversationId=Guid.NewGuid(),NativePeer="1234567890",AfterIncomingSequence=1,StartedAtUtc=DateTimeOffset.UtcNow,Phone="0900000000"};
        Assert.False(c.LiveReplyPilot.Valid(c));
    }
}
