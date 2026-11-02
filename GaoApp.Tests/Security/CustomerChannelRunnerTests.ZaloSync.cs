using System.Net;
using System.Text.Json;
using GaoApp.Web.Services.CustomerService;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GaoApp.Tests.Security;

public sealed partial class CustomerChannelRunnerTests
{
    private sealed class PagedInbox(Fixture f,int peers=1,string kind="Direct")
    {
        internal readonly Guid[] Peers=Enumerable.Range(0,peers).Select(i=>i==0?f.Peer:Guid.NewGuid()).ToArray();
        internal readonly Dictionary<Guid,List<object>> Messages=[];
        internal readonly List<(Guid Peer,long? Before)> Details=[];
        internal bool IgnoreBefore,DropAnchor,FailNextOlder,OutgoingOnly;
        internal string SourceStatus="Ready";
        internal bool SourceEnabled=true;
        internal Action? OnOlder;
        internal void Baseline(){foreach(var peer in Peers)Messages[peer]=[Frame(1,"BASELINE",f.Clock.Now.AddHours(-1))];}
        internal object Frame(long seq,string text,DateTimeOffset observed,bool outgoing=false)=>new{sequence=seq,text,kind="Text",observedAtUtc=observed,isOutgoing=outgoing};
        internal void Add(int count) {foreach(var peer in Peers)for(var i=0;i<count;i++)Messages[peer].Add(Frame(i+2,"TEST item "+(i+2),f.Clock.Now,OutgoingOnly));}
        internal HttpResponseMessage Read(HttpRequestMessage request) {
            var query=Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(request.RequestUri!.Query);
            if(request.RequestUri.AbsolutePath=="/api/v1/inbox/conversations") {
                var page=int.Parse(query["page"]!);return CustomerChannelTransportTests.Json(new{sync=new{enabled=SourceEnabled,status=SourceStatus},conversations=Peers.Skip((page-1)*25).Take(25).Select(p=>new{id=p,conversationKind=kind}),hasNextPage=page*25<Peers.Length});
            }
            var peer=Guid.Parse(request.RequestUri.Segments.Last());long? before=query.TryGetValue("before",out var raw)?long.Parse(raw!):null;Details.Add((peer,before));
            if(before is not null){OnOlder?.Invoke();if(FailNextOlder){FailNextOlder=false;return new(HttpStatusCode.ServiceUnavailable);}}
            var frames=Messages[peer].Select(m=>JsonSerializer.SerializeToElement(m)).Where(m=>!DropAnchor||m.GetProperty("sequence").GetInt64()!=1).ToList();
            // Match EfInboxService: before points to an identity in timeline order,
            // not a numeric comparison against sequence values.
            var end=before is null||IgnoreBefore?frames.Count:frames.FindIndex(m=>m.GetProperty("sequence").GetInt64()==before);
            if(end<0)end=0;var start=Math.Max(0,end-50);
            return CustomerChannelTransportTests.Json(new{id=peer,displayName="TEST synthetic",kind,hasOlder=start>0,messages=frames.Skip(start).Take(end-start)});
        }
    }
    private static CustomerMessage[] Imported(Fixture f,CustomerServiceStore? store=null) {
        store??=f.Store;return store.Inbox(f.Agent,"all",100).SelectMany(c=>store.StaffGet(f.Agent,c.Id).Messages).Where(m=>m.Role=="customer").ToArray();
    }
    private static async Task Drain(Fixture f,CustomerChannelRunner? runner=null,CustomerServiceStore? store=null,int limit=100) {
        runner??=f.Runner;store??=f.Store;
        for(var i=0;i<limit;i++){await runner.Run(f.Connection.Id,true,default);if(!store.ChannelState(f.Connection).SyncIncomplete)return;}
        Assert.Fail("Synthetic source did not finish within the test work budget.");
    }
    [Theory][InlineData(61,"Direct")][InlineData(200,"Direct")][InlineData(1000,"Direct")]
    [InlineData(61,"Group")][InlineData(200,"Group")][InlineData(1000,"Group")]
    public async Task Every_missing_message_is_imported_once_with_durable_pagination(int count,string kind) {
        await using var f=new Fixture(true);var source=new PagedInbox(f,kind:kind);source.Baseline();f.InboxSource=source.Read;
        await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(count);
        await f.Runner.Run(f.Connection.Id,true,default);
        if(count==1000){Assert.True(f.Store.ChannelState(f.Connection).SyncIncomplete);Assert.True(f.Store.ChannelState(f.Connection).BufferedPages>0);}
        await Drain(f);
        var imported=Imported(f);Assert.Equal(count+(kind=="Group"?1:0),imported.Length);Assert.Equal(imported.Length,imported.Select(m=>m.Zalo!.Sequence).Distinct().Count());
        Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);Assert.Null(f.Store.ChannelState(f.Connection).Error);
        var final=Assert.Single(f.Store.Inbox(f.Agent,"waiting",100));Assert.Equal(count+1,f.Store.StaffGet(f.Agent,final.Id).Messages.Last(m=>m.Role=="customer").Zalo!.Sequence);
        await Drain(f);Assert.Equal(imported.Length,Imported(f).Length);
    }
    [Fact]public async Task Interrupted_history_resumes_after_store_and_runner_restart_without_skipping_or_duplicates() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);
        var success=f.Store.ChannelState(f.Connection).LastSuccess;f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(200);source.FailNextOlder=true;
        await f.Runner.Run(f.Connection.Id,true,default);var failed=f.Store.ChannelState(f.Connection);
        Assert.Equal("zalo_sync_unavailable",failed.Error);Assert.Equal(success,failed.LastSuccess);Assert.Empty(Imported(f));Assert.Equal(1,failed.BufferedPages);
        var store=new CustomerServiceStore(new AssistantIntegrationTests.EnvironmentStub(f.DirectoryPath),new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new(){DatabasePath="test.db"}),f.Clock);
        await Drain(f,f.MakeRunner(store),store);Assert.Equal(200,Imported(f,store).Length);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }
    [Fact]public async Task Missing_checkpoint_never_reports_success_or_replies_even_when_new_data_is_imported() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);
        var success=f.Store.ChannelState(f.Connection).LastSuccess;f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);source.DropAnchor=true;
        await f.Runner.Run(f.Connection.Id,true,default);Assert.Equal("zalo_sync_anchor_missing",f.Store.ChannelState(f.Connection).Error);Assert.Equal(success,f.Store.ChannelState(f.Connection).LastSuccess);Assert.Empty(Imported(f));
        f.Store.ImportChannel(f.Connection,new("TEST separate event",f.Peer.ToString(),"TEST receipt",f.Clock.Now,"99",true));
        Assert.Equal("zalo_sync_anchor_missing",f.Store.ChannelState(f.Connection).Error);Assert.Equal(success,f.Store.ChannelState(f.Connection).LastSuccess);Assert.NotNull(f.Store.ChannelState(f.Connection).LastReceived);
        Assert.Null(f.Store.TakeChannelTurn(f.Connection));Assert.Equal(0,f.AiCalls);
        source.DropAnchor=false;await Drain(f);Assert.Equal(62,Imported(f).Length);
    }
    [Fact]public async Task Unsupported_pagination_is_visible_and_can_recover_when_source_is_fixed() {
        await using var f=new Fixture();var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);source.IgnoreBefore=true;
        await f.Runner.Run(f.Connection.Id,true,default);Assert.Equal("zalo_sync_cursor_not_advancing",f.Store.ChannelState(f.Connection).Error);Assert.Empty(Imported(f));
        source.IgnoreBefore=false;await Drain(f);Assert.Equal(61,Imported(f).Length);
    }
    [Fact]public async Task Many_conversation_pages_keep_progress_and_complete_only_after_the_last_page() {
        await using var f=new Fixture();var source=new PagedInbox(f,61);source.Baseline();f.InboxSource=source.Read;
        await f.Runner.Run(f.Connection.Id,true,default);Assert.Null(f.Store.ChannelState(f.Connection).LastSuccess);Assert.True(f.Store.ChannelState(f.Connection).SyncIncomplete);
        await Drain(f);Assert.Equal(61,source.Details.Select(d=>d.Peer).Distinct().Count());f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(1);await Drain(f);Assert.Equal(61,Imported(f).Length);
    }
    [Fact]public async Task Backfilled_sequences_are_not_treated_as_a_numeric_watermark() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);
        source.Messages[f.Peer].Insert(1,source.Frame(9999,"TEST restored older frame",f.Clock.Now));await Drain(f);
        Assert.Equal(62,Imported(f).Length);Assert.Equal(0,f.AiCalls);
    }
    [Fact]public async Task Paused_mid_fetch_retains_progress_and_resumes_without_importing_while_paused() {
        await using var f=new Fixture();var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(200);
        source.OnOlder=()=>{source.OnOlder=null;var state=f.Store.ChannelState(f.Connection);f.Store.PauseChannel(f.Connection,true,state.Version);};
        await f.Runner.Run(f.Connection.Id,true,default);Assert.True(f.Store.ChannelState(f.Connection).Paused);Assert.Empty(Imported(f));Assert.True(f.Store.ChannelState(f.Connection).SyncIncomplete);
        var paused=f.Store.ChannelState(f.Connection);f.Store.PauseChannel(f.Connection,false,paused.Version);await Drain(f);Assert.Equal(200,Imported(f).Length);
    }
    [Fact]public async Task Outgoing_only_frames_advance_history_cursor_without_incoming_or_send_side_effects() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.OutgoingOnly=true;source.Add(200);await Drain(f);
        Assert.Empty(Imported(f));Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);Assert.False(f.Store.ChannelState(f.Connection).SyncIncomplete);
    }
    [Fact]public async Task Durable_sync_lease_prevents_an_overlapping_runner_from_replacing_the_checkpoint() {
        await using var f=new Fixture();var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;
        var lease=f.Store.AcquireZaloSync(f.Connection);Assert.NotNull(lease);await f.Runner.Run(f.Connection.Id,true,default);Assert.Empty(source.Details);Assert.Null(f.Store.AcquireZaloSync(f.Connection));
        f.Store.ReleaseZaloSync(f.Connection,lease!.Value);await Drain(f);Assert.Single(source.Details);
    }
    [Fact]public async Task Receipt_committed_before_page_progress_survives_restart_without_duplicate_or_AI() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);
        JsonElement Page(IEnumerable<object> frames,bool more)=>JsonSerializer.SerializeToElement(new{id=f.Peer,displayName="TEST synthetic",kind="Direct",hasOlder=more,messages=frames});
        var all=source.Messages[f.Peer];var work=new CustomerZaloSyncWork{Started=f.Clock.Now.ToUnixTimeMilliseconds(),Peers=[new(f.Peer,"Direct")],History=new(){Anchor=1,Covered=true,Pages=[Page(all.Skip(12),true),Page(all.Take(12),false)]}};
        var lease=f.Store.AcquireZaloSync(f.Connection)!.Value;f.Store.SaveZaloSync(f.Connection,lease,work);
        for(var i=2;i<=6;i++)f.Store.ImportChannel(f.Connection,new(f.Peer.ToString("D")+":"+i,f.Peer.ToString("D"),"TEST item "+i,f.Clock.Now,i.ToString(),true,"TEST synthetic","Direct",new(i,"Text",null,null,[])));
        f.Store.ReleaseZaloSync(f.Connection,lease);await Drain(f);Assert.Equal(61,Imported(f).Length);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }
    [Fact]public async Task Expired_staging_erases_message_bodies_retains_warning_and_manager_retry_checks_current_window() {
        await using var f=new Fixture();var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(1000);
        await f.Runner.Run(f.Connection.Id,true,default);Assert.True(f.Store.ChannelState(f.Connection).BufferedPages>0);
        f.Clock.Now=f.Clock.Now.AddDays(8);var state=f.Store.ChannelState(f.Connection);Assert.Equal("zalo_sync_history_expired",state.Error);Assert.True(state.SyncIncomplete);Assert.Equal(0,state.BufferedPages);
        using(var db=new SqliteConnection("Data Source="+Path.Combine(f.DirectoryPath,"test.db")+";Pooling=False")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT Work FROM CskhZaloSync";Assert.DoesNotContain("TEST item",(string)cmd.ExecuteScalar()!);}
        Assert.Throws<GaoApp.Web.Services.Assistant.AssistantException>(()=>f.Store.RetryZaloSync(f.Agent with{CanManage=false},f.Connection,state.Version));
        f.Store.RetryZaloSync(f.Agent,f.Connection,state.Version);Assert.True(f.Store.ChannelState(f.Connection).SyncIncomplete);await Drain(f);Assert.Empty(Imported(f));
    }
    [Fact]public async Task Manager_retry_is_versioned_and_cannot_override_a_live_sync_lease_or_another_store() {
        await using var f=new Fixture();var lease=f.Store.AcquireZaloSync(f.Connection)!.Value;
        Assert.Throws<GaoApp.Web.Services.Assistant.AssistantException>(()=>f.Store.RetryZaloSync(f.Agent,f.Connection,0));f.Store.ReleaseZaloSync(f.Connection,lease);
        Assert.Throws<GaoApp.Web.Services.Assistant.AssistantException>(()=>f.Store.RetryZaloSync(f.Agent with{StoreId=2},f.Connection,0));
        f.Store.RetryZaloSync(f.Agent,f.Connection,0);Assert.Throws<GaoApp.Web.Services.Assistant.AssistantException>(()=>f.Store.RetryZaloSync(f.Agent,f.Connection,0));
        Assert.True(f.Store.ChannelState(f.Connection).SyncIncomplete);Assert.Null(f.Store.ChannelState(f.Connection).LastSuccess);
    }
    [Fact]public async Task Additive_schema_upgrade_preserves_existing_channel_messages_and_receipts() {
        await using var f=new Fixture();await f.Receive();var before=Imported(f);Assert.Single(before);
        using(var db=new SqliteConnection("Data Source="+Path.Combine(f.DirectoryPath,"test.db")+";Pooling=False")){db.Open();using var cmd=db.CreateCommand();cmd.CommandText="DROP TABLE CskhZaloCheckpoints; DROP TABLE CskhZaloSync; PRAGMA user_version=6;";cmd.ExecuteNonQuery();}
        var reopened=new CustomerServiceStore(new AssistantIntegrationTests.EnvironmentStub(f.DirectoryPath),new AssistantIntegrationTests.Monitor<CustomerServiceOptions>(new(){DatabasePath="test.db"}),f.Clock);
        Assert.Null(reopened.ChannelState(f.Connection).LastSuccess);Assert.True(reopened.ChannelState(f.Connection).SyncIncomplete);Assert.Equal(before.Select(m=>m.Id),Imported(f,reopened).Select(m=>m.Id));Assert.True(reopened.ChannelSeen(f.Connection,f.Peer.ToString("D")+":2"));
        f.InboxSource=r=>r.RequestUri!.AbsolutePath=="/api/v1/inbox/conversations"?CustomerChannelTransportTests.Json(new{sync=new{enabled=true,status="Ready"},conversations=new[]{new{id=f.Peer,conversationKind="Direct"}},hasNextPage=false}):CustomerChannelTransportTests.Json(new{id=f.Peer,kind="Direct",displayName="TEST empty source",hasOlder=false,messages=Array.Empty<object>()});
        await f.MakeRunner(reopened).Run(f.Connection.Id,true,default);Assert.Equal("zalo_sync_anchor_missing",reopened.ChannelState(f.Connection).Error);Assert.Null(reopened.ChannelState(f.Connection).LastSuccess);f.InboxSource=null;
        await Drain(f,f.MakeRunner(reopened),reopened);Assert.Equal(before.Select(m=>m.Id),Imported(f,reopened).Select(m=>m.Id));
    }
    [Fact]public async Task A_restored_incoming_frame_before_an_outgoing_checkpoint_is_always_staff_history() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();source.Messages[f.Peer][0]=source.Frame(1,"TEST outbound baseline",f.Clock.Now.AddHours(-1),true);f.InboxSource=source.Read;await Drain(f);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Messages[f.Peer].Insert(0,source.Frame(9999,"TEST restored before outbound",f.Clock.Now));await Drain(f);
        Assert.Single(Imported(f));Assert.Equal("waiting",Assert.Single(f.Store.Inbox(f.Agent,"all")).Mode);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }
    [Theory][InlineData("Direct")][InlineData("Group")]
    public async Task Paged_backfill_keeps_media_metadata_and_attachment_on_its_exact_source_message(string kind) {
        await using var f=new Fixture(true);var source=new PagedInbox(f,kind:kind);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);
        var asset=Guid.NewGuid();source.Messages[f.Peer][30]=new{sequence=31,text="TEST image in backfill",kind="Image",observedAtUtc=f.Clock.Now,isOutgoing=false,senderName="TEST member",attachments=new[]{new{id=asset,kind="Image",fileName="test.png",contentType="image/png",length=100L}}};
        await Drain(f);var message=Assert.Single(Imported(f),m=>m.Zalo?.Sequence==31);Assert.Equal(asset,Assert.Single(message.Zalo!.Attachments).Id);Assert.Equal("TEST member",message.Zalo.SenderName);Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
    }
    [Fact]public async Task Older_pages_must_prove_the_same_sender_account_before_any_buffered_data_is_imported() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(61);source.OnOlder=()=>f.WrongProfile=true;
        await f.Runner.Run(f.Connection.Id,true,default);Assert.Equal("zalo_contract_or_profile_mismatch",f.Store.ChannelState(f.Connection).Error);Assert.Empty(Imported(f));Assert.Equal(0,f.AiCalls);Assert.Equal(0,f.Sends);
        source.OnOlder=null;f.WrongProfile=false;await Drain(f);Assert.Equal(61,Imported(f).Length);
    }
    [Fact]public async Task Restored_history_behind_the_checkpoint_and_outside_the_latest_page_is_not_silently_skipped() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();f.InboxSource=source.Read;await Drain(f);f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Add(200);await Drain(f);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Messages[f.Peer].Insert(1,source.Frame(99999,"TEST restored beyond latest page",f.Clock.Now));source.Details.Clear();await Drain(f);
        Assert.Equal(201,Imported(f).Length);Assert.Contains(Imported(f),m=>m.Zalo?.Sequence==99999);Assert.True(source.Details.Count>1);Assert.Equal(0,f.AiCalls);
        var active=Assert.Single(f.Store.Inbox(f.Agent,"waiting"));var claimed=f.Store.StaffCommand(f.Agent,active.Id,new(Guid.NewGuid(),"claim",active.Version));
        f.Store.StaffCommand(f.Agent,active.Id,new(Guid.NewGuid(),"reply",claimed.Version,"TEST reply after restored history"));
        Assert.Equal("201",f.Store.TakeChannelDispatch(f.Connection)!.Reference);
    }
    [Fact]public async Task A_fresh_tail_can_use_AI_after_the_retained_history_has_been_verified() {
        await using var f=new Fixture(true);var source=new PagedInbox(f);source.Baseline();for(var i=2;i<=201;i++)source.Messages[f.Peer].Add(source.Frame(i,"TEST old baseline",f.Clock.Now.AddHours(-1)));f.InboxSource=source.Read;await Drain(f);
        f.Clock.Now=f.Clock.Now.AddMinutes(1);source.Messages[f.Peer].Add(source.Frame(202,"TEST sữa tươi",f.Clock.Now));await Drain(f);
        Assert.Single(Imported(f));Assert.Equal(1,f.AiCalls);Assert.Equal(1,f.Sends);
    }
}
