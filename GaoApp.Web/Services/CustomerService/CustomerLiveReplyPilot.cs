using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

// Explicit host-side pilot. The SDK independently resolves the test phone again
// before dispatch. This setting never asserts customer identity or complete history.
public sealed class CustomerLiveReplyPilot
{
    public bool Enabled {get;set;}
    public string Phone {get;set;}="0398946174";
    public Guid ConversationId {get;set;}
    public string NativePeer {get;set;}="";
    public long AfterIncomingSequence {get;set;}
    public DateTimeOffset StartedAtUtc {get;set;}
    public bool Valid(CustomerChannelConnection c)=>!Enabled||(c.Provider=="zalomarketing"&&c.Enabled&&c.AutoReply&&Phone=="0398946174"&&
        ConversationId!=Guid.Empty&&Regex.IsMatch(NativePeer,@"\A[1-9][0-9]{0,31}\z")&&AfterIncomingSequence>0&&StartedAtUtc> DateTimeOffset.UnixEpoch);
    public bool Allows(string peer,string reference,DateTimeOffset created)=>Enabled&&peer==ConversationId.ToString("D")&&
        long.TryParse(reference,NumberStyles.None,CultureInfo.InvariantCulture,out var sequence)&&sequence>AfterIncomingSequence&&created>=StartedAtUtc;
}

public sealed partial class CustomerServiceStore
{
    public void PrepareZaloLivePilot(CustomerChannelConnection c)=>Run((db,tx)=>{
        if(!c.LiveReplyPilot.Enabled||!c.LiveReplyPilot.Valid(c))return false;
        var id=Scalar(db,tx,"SELECT c.Id FROM CskhConversations c JOIN CskhChannelThreads t ON t.Conversation=c.Id WHERE c.Store=$store AND c.Mode!='closed' AND c.Expires>$now AND t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer ORDER BY c.Created DESC LIMIT 1",
            ("$store",c.StoreId),("$now",Now),("$connection",c.Id),("$binding",c.Binding),("$peer",c.LiveReplyPilot.ConversationId.ToString("D"))) as string;
        if(id is null)return false;
        var digest=System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(c.Binding+"\n"+c.LiveReplyPilot.StartedAtUtc.ToUnixTimeMilliseconds()));
        var request=new Guid(digest[..16]);
        if(Replay(db,tx,Guid.Parse(id),request,"host-configuration:live-pilot",c.LiveReplyPilot))return false;
        // One activation only. Release the previous history-only waiting state;
        // never take over a claimed conversation or a later human handoff.
        Execute(db,tx,"UPDATE CskhConversations SET Mode='bot',Reason=NULL,Version=Version+1,Updated=$now WHERE Id=$id AND Mode='waiting' AND Owner IS NULL AND Reason='channel_needs_human' AND ProcessingId IS NULL",
            ("$id",id),("$now",Now));return true;
    });
    private bool ZaloLivePilotReady(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c)
    {
        if(!c.LiveReplyPilot.Enabled||!c.LiveReplyPilot.Valid(c))return false;
        var state=ChannelState(db,tx,c);
        if(state.Error!="zalo_source_history_incomplete"||!state.Baseline||state.Cursor!=1)return false;
        return Scalar(db,tx,"SELECT 1 FROM CskhZaloSync s JOIN CskhZaloCheckpoints p ON p.Connection=s.Connection WHERE s.Connection=$c AND s.Binding=$b AND s.Work IS NULL AND s.LastCompleted>$cutoff AND p.Peer=$peer AND p.Verified>$cutoff",
            ("$c",c.Id),("$b",c.Binding),("$peer",c.LiveReplyPilot.ConversationId.ToString("D")),("$cutoff",Now-90000)) is not null;
    }
}

public sealed partial class CustomerChannelRunner
{
    private async Task PollZaloLivePilot(CustomerChannelConnection c,CancellationToken ct)
    {
        var pilot=c.LiveReplyPilot;if(!pilot.Valid(c))throw new CustomerZaloSyncException("zalo_live_pilot_invalid");
        var lease=storage.AcquireZaloSync(c);if(lease is null)return;
        try
        {
            var work=new CustomerZaloSyncWork{Started=clock.GetUtcNow().ToUnixTimeMilliseconds(),SourceReady=false,Peers=[new(pilot.ConversationId,"Direct")]};
            storage.SaveZaloSync(c,lease.Value,work);
            var page=await transport.ZaloGet(c,"api/v1/inbox/conversations?page=1",ct);
            var sync=page.GetProperty("sync");var status=sync.GetProperty("status").GetString();
            if(!sync.GetProperty("enabled").GetBoolean()||status is not ("Ready" or "Waiting")||
                !sync.TryGetProperty("lastAttemptAtUtc",out var attempt)||attempt.ValueKind!=JsonValueKind.String||
                attempt.GetDateTimeOffset()<clock.GetUtcNow().AddSeconds(-90)||attempt.GetDateTimeOffset()>clock.GetUtcNow().AddSeconds(5))
                throw new CustomerZaloSyncException("zalo_source_not_ready");
            var detail=await transport.ZaloGet(c,$"api/v1/inbox/conversations/{pilot.ConversationId:D}",ct);
            ValidateZaloPage(detail,new(pilot.ConversationId,"Direct"),new());
            var proof=detail.GetProperty("recipientEvidence");
            if(proof.GetProperty("transport").GetString()!="Sdk"||proof.GetProperty("kind").GetString()!="Direct"||
                proof.GetProperty("senderProfileId").GetGuid()!=Guid.Parse(c.AccountId)||proof.GetProperty("conversationId").GetGuid()!=pilot.ConversationId||
                proof.GetProperty("nativePeer").GetString()!=pilot.NativePeer)throw new CustomerZaloSyncException("zalo_live_pilot_peer_mismatch");
            var messages=detail.GetProperty("messages").EnumerateArray().ToArray();
            var incoming=messages.Where(m=>!m.GetProperty("isOutgoing").GetBoolean()).ToArray();
            if(incoming.Length==0)throw new CustomerZaloSyncException("zalo_live_pilot_evidence_missing");
            var anchor=incoming[^1];var sequence=anchor.GetProperty("sequence").GetInt64();
            var observed=anchor.GetProperty("observedAtUtc").GetDateTimeOffset();
            if(proof.GetProperty("latestIncomingSequence").GetInt64()!=sequence||proof.GetProperty("observedAtUtc").GetDateTimeOffset()!=observed||
                incoming.Any(m=>m.GetProperty("senderId").GetString()!=pilot.NativePeer)||observed>clock.GetUtcNow().AddSeconds(5))
                throw new CustomerZaloSyncException("zalo_live_pilot_peer_mismatch");
            storage.PrepareZaloLivePilot(c);
            foreach(var m in incoming)
            {
                ct.ThrowIfCancellationRequested();c=registry.Get(c.Id);storage.CheckZaloSync(c,lease.Value);
                if(!c.LiveReplyPilot.Enabled||c.LiveReplyPilot.ConversationId!=pilot.ConversationId||c.LiveReplyPilot.NativePeer!=pilot.NativePeer||
                    c.LiveReplyPilot.AfterIncomingSequence!=pilot.AfterIncomingSequence||c.LiveReplyPilot.StartedAtUtc!=pilot.StartedAtUtc)
                    throw new CustomerZaloSyncPausedException();
                var seq=m.GetProperty("sequence").GetInt64();var at=m.GetProperty("observedAtUtc").GetDateTimeOffset();
                if(seq<=pilot.AfterIncomingSequence||at<pilot.StartedAtUtc||at<clock.GetUtcNow().AddMinutes(-5))continue;
                var metadata=CustomerZaloMedia.Parse(m,seq);var text=m.GetProperty("text").GetString();
                if(string.IsNullOrWhiteSpace(text))text="[Nội dung đính kèm]";
                var needsHuman=metadata.Kind!="Text"||metadata.Attachments.Length>0||text.Length>2000;
                var input=new CustomerChannelIncoming($"{pilot.ConversationId:D}:{seq}",pilot.ConversationId.ToString("D"),text,at,
                    seq.ToString(CultureInfo.InvariantCulture),needsHuman,CustomerZaloMedia.Clean(detail.GetProperty("displayName").GetString(),80),"Direct",metadata);
                if(storage.ChannelSeen(c,input.ExternalId)){storage.RefreshZaloMessage(c,input);continue;}
                if(storage.ImportChannel(c,input) is null)throw new CustomerZaloSyncPausedException();
                if(CustomerMarketingOptOut.IsOptOut(text))await RecordMarketingOptOutAsync(c,pilot.ConversationId,seq,ct);
            }
            work.Position=1;
            storage.FinishZaloPeer(c,lease.Value,pilot.ConversationId,messages[^1].GetProperty("sequence").GetInt64(),work,new(sequence,observed));
            storage.FinishZaloPage(c,lease.Value,false,sourceReady:false);
        }
        finally {storage.ReleaseZaloSync(c,lease.Value);}
    }
}
