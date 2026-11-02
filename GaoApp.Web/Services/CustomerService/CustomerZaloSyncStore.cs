using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

// This cursor is a message identity, never a numeric high-water mark: Zalo can insert
// an older timeline frame with a larger sequence. Buffered pages stay in private SQLite.
public sealed record CustomerZaloSyncPeer(Guid Id,string Kind);
public sealed record CustomerZaloSyncReference(long Sequence,DateTimeOffset ObservedAt);
public sealed class CustomerZaloSyncWork
{
    public long Started {get;set;}
    public bool SourceReady {get;set;}=true;
    public CustomerZaloSyncPeer[]? Peers {get;set;}
    public bool More {get;set;}
    public int Position {get;set;}
    public CustomerZaloSyncHistory? History {get;set;}
}
public sealed class CustomerZaloSyncHistory
{
    public long? Anchor {get;set;}
    public List<JsonElement> Pages {get;set;}=[];
    public bool Covered {get;set;}
    public bool AnchorFound {get;set;}
    public bool LatestRangeCovered {get;set;}
    public int ImportedPages {get;set;}
    public int LastKnownPosition {get;set;}=-1;
}
public sealed class CustomerZaloSyncException(string code):Exception { public string Code=>code; }
public sealed class CustomerZaloSyncPausedException:Exception;

public sealed partial class CustomerServiceStore
{
    private CustomerChannelState WithZaloSyncState(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c,CustomerChannelState state) {
        if(c.Provider!="zalomarketing")return state;
        ExpireZaloWork(db,tx,c.Id);
        state=state with{Error=Scalar(db,tx,"SELECT Error FROM CskhChannelControls WHERE Connection=$c",("$c",c.Id)) as string};
        using var cmd=Command(db,tx,"SELECT LastReceived,Work IS NOT NULL,Peer,Pages,LastCompleted FROM CskhZaloSync WHERE Connection=$c AND Binding=$b",("$c",c.Id),("$b",c.Binding));
        using var r=cmd.ExecuteReader();
        var received=(DateTimeOffset?)null;var completed=(DateTimeOffset?)null;var pending=true;string? peer=null;var pages=0;
        if(r.Read()){received=r.IsDBNull(0)?null:DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(0));pending=r.GetInt64(1)==1;peer=r.IsDBNull(2)?null:r.GetString(2);pages=r.GetInt32(3);completed=r.IsDBNull(4)?null:DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(4));}
        return state with{LastReceived=received,LastSuccess=completed,SyncIncomplete=completed is null||pending||state.Error is not null||!state.Baseline||state.Cursor!=1,SyncPeer=peer,BufferedPages=pages};
    }
    public Guid? AcquireZaloSync(CustomerChannelConnection c)=>Run((db,tx)=>{
        if(!c.Enabled||ChannelState(db,tx,c).Paused||!Settings(db,tx,c.StoreId).Enabled)return null;
        Execute(db,tx,"INSERT INTO CskhZaloSync(Connection,Binding) VALUES($c,$b) ON CONFLICT(Connection) DO NOTHING",("$c",c.Id),("$b",c.Binding));
        var lease=Guid.NewGuid();using var cmd=Command(db,tx,"UPDATE CskhZaloSync SET Lease=$l,LeaseUntil=$until WHERE Connection=$c AND Binding=$b AND (Lease IS NULL OR LeaseUntil<=$now)",("$l",lease.ToString()),("$until",Now+90000),("$c",c.Id),("$b",c.Binding),("$now",Now));
        return cmd.ExecuteNonQuery()==1?(Guid?)lease:null;
    });
    private void ZaloSyncActive(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c,Guid lease) {
        if(!c.Enabled||ChannelState(db,tx,c).Paused||!Settings(db,tx,c.StoreId).Enabled||
            Scalar(db,tx,"SELECT Lease FROM CskhZaloSync WHERE Connection=$c AND Binding=$b AND LeaseUntil>$now",("$c",c.Id),("$b",c.Binding),("$now",Now)) as string!=lease.ToString())throw new CustomerZaloSyncPausedException();
    }
    public void CheckZaloSync(CustomerChannelConnection c,Guid lease)=>Run((db,tx)=>{ZaloSyncActive(db,tx,c,lease);return true;});
    public CustomerZaloSyncWork? ReadZaloSync(CustomerChannelConnection c,Guid lease)=>Run((db,tx)=>{
        ZaloSyncActive(db,tx,c,lease);return Scalar(db,tx,"SELECT Work FROM CskhZaloSync WHERE Connection=$c",("$c",c.Id)) is string json?JsonSerializer.Deserialize<CustomerZaloSyncWork>(json):null;
    });
    public long? ZaloSyncAnchor(CustomerChannelConnection c,Guid peer)=>Run((db,tx)=>{
        ChannelState(db,tx,c);var value=Scalar(db,tx,"SELECT Anchor FROM CskhZaloCheckpoints WHERE Connection=$c AND Peer=$p AND Verified>$cutoff",("$c",c.Id),("$p",peer.ToString("D")),("$cutoff",Now-7L*86400000));
        if(value is long n)return (long?)n;
        // On upgrade the old thread's reference is evidence too; an empty/truncated
        // source must not silently replace it with a new successful baseline.
        var previous=Scalar(db,tx,"SELECT t.Reference FROM CskhChannelThreads t JOIN CskhConversations c ON c.Id=t.Conversation WHERE t.Connection=$c AND t.Binding=$b AND t.Peer=$p AND c.Store=$store AND c.Expires>$now AND t.LastInbound>$cutoff ORDER BY t.LastInbound DESC,t.rowid DESC LIMIT 1",("$c",c.Id),("$b",c.Binding),("$p",peer.ToString("D")),("$store",c.StoreId),("$cutoff",Now-7L*86400000),("$now",Now)) as string;
        return long.TryParse(previous,out var old)&&old>0?(long?)old:null;
    });
    public void SaveZaloSync(CustomerChannelConnection c,Guid lease,CustomerZaloSyncWork work)=>Run((db,tx)=>{
        ZaloSyncActive(db,tx,c,lease);var json=JsonSerializer.Serialize(work);
        if(json.Length>16*1024*1024)throw new CustomerZaloSyncException("zalo_sync_buffer_capacity");
        WriteZaloWork(db,tx,c,work,json);
        Execute(db,tx,"UPDATE CskhChannelControls SET Started=COALESCE(Started,$now),Error='zalo_sync_incomplete' WHERE Connection=$c",("$c",c.Id),("$now",Now));return true;
    });
    public void FinishZaloPeer(CustomerChannelConnection c,Guid lease,Guid peer,long? anchor,CustomerZaloSyncWork next,CustomerZaloSyncReference? reference=null)=>Run((db,tx)=>{
        ZaloSyncActive(db,tx,c,lease);
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhZaloCheckpoints WHERE Connection=$c",("$c",c.Id))!>=100000&&Scalar(db,tx,"SELECT 1 FROM CskhZaloCheckpoints WHERE Connection=$c AND Peer=$p",("$c",c.Id),("$p",peer.ToString("D"))) is null)throw new CustomerZaloSyncException("zalo_sync_peer_capacity");
        Execute(db,tx,"INSERT INTO CskhZaloCheckpoints VALUES($c,$p,$anchor,$now) ON CONFLICT(Connection,Peer) DO UPDATE SET Anchor=excluded.Anchor,Verified=excluded.Verified",("$c",c.Id),("$p",peer.ToString("D")),("$anchor",anchor),("$now",Now));
        // ObservedAt is capture time: a restored old frame may have a newer value.
        // Pin future replies to the latest incoming frame in source timeline order.
        // Existing outbox rows retain their already frozen reference/idempotency key.
        if(reference is not null)Execute(db,tx,"UPDATE CskhChannelThreads SET Reference=$reference,LastInbound=$at WHERE Connection=$c AND Binding=$b AND Peer=$p AND Conversation IN(SELECT Id FROM CskhConversations WHERE Store=$store AND Mode!='closed')",("$reference",reference.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)),("$at",reference.ObservedAt.ToUnixTimeMilliseconds()),("$c",c.Id),("$b",c.Binding),("$p",peer.ToString("D")),("$store",c.StoreId));
        WriteZaloWork(db,tx,c,next,JsonSerializer.Serialize(next));return true;
    });
    public void FinishZaloPage(CustomerChannelConnection c,Guid lease,bool more,bool sourceReady=true)=>Run((db,tx)=>{
        ZaloSyncActive(db,tx,c,lease);var state=ChannelState(db,tx,c);
        if(more&&state.Cursor>=10000)throw new CustomerZaloSyncException("zalo_inbox_page_limit");
        Execute(db,tx,"UPDATE CskhZaloSync SET Work=NULL,WorkStarted=NULL,Peer=NULL,Pages=0,LastCompleted=CASE WHEN $more=0 THEN $now ELSE LastCompleted END WHERE Connection=$c",("$more",more?1:0),("$now",Now),("$c",c.Id));
        Execute(db,tx,"UPDATE CskhChannelControls SET Cursor=$cursor,Baseline=CASE WHEN $more=0 THEN 1 ELSE Baseline END,LastSuccess=CASE WHEN $more=0 THEN $now ELSE LastSuccess END,Error=CASE WHEN $more=0 THEN $sourceError ELSE 'zalo_sync_incomplete' END WHERE Connection=$c",("$cursor",more?state.Cursor+1:1),("$more",more?1:0),("$sourceError",sourceReady?null:"zalo_source_history_incomplete"),("$now",Now),("$c",c.Id));return true;
    });
    public void ReleaseZaloSync(CustomerChannelConnection c,Guid lease)=>Run((db,tx)=>{Execute(db,tx,"UPDATE CskhZaloSync SET Lease=NULL,LeaseUntil=NULL WHERE Connection=$c AND Binding=$b AND Lease=$l",("$c",c.Id),("$b",c.Binding),("$l",lease.ToString()));return true;});
    public void RetryZaloSync(CustomerStaff who,CustomerChannelConnection c,long version)=>Run((db,tx)=>{
        if(!who.CanManage||who.StoreId!=c.StoreId)throw AssistantException.Denied();
        if(c.Provider!="zalomarketing")throw AssistantException.Invalid();
        if(ChannelState(db,tx,c).Version!=version||Scalar(db,tx,"SELECT 1 FROM CskhZaloSync WHERE Connection=$c AND Lease IS NOT NULL AND LeaseUntil>$now",("$c",c.Id),("$now",Now)) is not null)throw Conflict();
        Execute(db,tx,"UPDATE CskhZaloSync SET Work=NULL,WorkStarted=NULL,Peer=NULL,Pages=0,Lease=NULL,LeaseUntil=NULL WHERE Connection=$c",("$c",c.Id));
        Execute(db,tx,"UPDATE CskhChannelControls SET Cursor=1,Error='zalo_sync_incomplete',Version=Version+1 WHERE Connection=$c",("$c",c.Id));return true;
    });
    private bool ZaloSyncBlocked(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c) {
        if(c.Provider!="zalomarketing")return false;var state=ChannelState(db,tx,c);
        return state.Error is not null||!state.Baseline||state.Cursor!=1||Scalar(db,tx,"SELECT 1 FROM CskhZaloSync WHERE Connection=$c AND LastCompleted IS NOT NULL",("$c",c.Id)) is null||Scalar(db,tx,"SELECT 1 FROM CskhZaloSync WHERE Connection=$c AND Work IS NOT NULL",("$c",c.Id)) is long;
    }
    private void WriteZaloWork(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection c,CustomerZaloSyncWork work,string json)=>
        Execute(db,tx,"UPDATE CskhZaloSync SET Work=$work,WorkStarted=$started,Peer=$peer,Pages=$pages,LeaseUntil=$until WHERE Connection=$c",("$work",json),("$started",work.Started),("$peer",work.Peers is {} peers&&work.Position<peers.Length?peers[work.Position].Id.ToString("D"):null),("$pages",work.History?.Pages.Count??0),("$until",Now+90000),("$c",c.Id));
}
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record CustomerZaloSyncRetry([property:System.Text.Json.Serialization.JsonRequired]long Version);
