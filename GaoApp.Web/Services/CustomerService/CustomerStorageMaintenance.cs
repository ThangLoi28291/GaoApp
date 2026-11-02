using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerCleanupResult(int Changed,int ExpiredConversations,bool More);
public sealed record CustomerStorageStatus(long Bytes,long ReservedSessions,long MaximumSessions);

public sealed partial class CustomerServiceStore
{
    private static readonly Meter StorageMeter=new("GaoApp.CustomerService.Storage","1.0");
    private static readonly Histogram<double> OperationDuration=StorageMeter.CreateHistogram<double>("cskh.storage.duration","ms");
    private static readonly Counter<long> StorageErrors=StorageMeter.CreateCounter<long>("cskh.storage.errors");
    private static readonly Counter<long> SessionRejections=StorageMeter.CreateCounter<long>("cskh.sessions.rejected");
    private static readonly Counter<long> CleanupChanges=StorageMeter.CreateCounter<long>("cskh.cleanup.changes");
    private static readonly Histogram<long> StorageBytes=StorageMeter.CreateHistogram<long>("cskh.storage.bytes","By");
    private static readonly Histogram<long> ReservedVisitors=StorageMeter.CreateHistogram<long>("cskh.sessions.reserved","{session}");
    private long lastStorageWarning=-60000;
    private void WarnStorage(string reason) {
        var now=Environment.TickCount64;var previous=Interlocked.Read(ref lastStorageWarning);
        if(now-previous>=60000&&Interlocked.CompareExchange(ref lastStorageWarning,now,previous)==previous)
            logger?.LogWarning("Customer service storage requires attention: {Reason}",reason);
    }
    private void StorageDuration(string operation,long started) {
        var elapsed=Stopwatch.GetElapsedTime(started).TotalMilliseconds;OperationDuration.Record(elapsed,new KeyValuePair<string,object?>("operation",operation));
        if(elapsed>=options.CurrentValue.StorageSlowMilliseconds)WarnStorage("slow_operation");
    }
    private void StorageFailure(Exception error) {
        var reason=error is SqliteException {SqliteErrorCode:5 or 6}?"busy":"unavailable";
        StorageErrors.Add(1,new KeyValuePair<string,object?>("reason",reason));WarnStorage(reason);
    }
    private void VisitorRejected(string reason){SessionRejections.Add(1,new KeyValuePair<string,object?>("reason",reason));WarnStorage("visitor_"+reason);}
    private T RunRead<T>(Func<SqliteConnection,SqliteTransaction,T> action,[System.Runtime.CompilerServices.CallerMemberName]string operation="")=>Run(action,readOnly:true,operation:operation);
    private int ChangeRows(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args) {using var cmd=Command(db,tx,sql,args);return cmd.ExecuteNonQuery();}
    private void RecoverConversation(SqliteConnection db,SqliteTransaction tx,Guid id,int? store=null) {
        if(ChangeRows(db,tx,"UPDATE CskhConversations SET Mode='waiting',Reason='ai_interrupted',Version=Version+1,Updated=$now,Unread=Unread+1,ProcessingId=NULL,ProcessingUntil=NULL WHERE Id=$id AND ($store IS NULL OR Store=$store) AND Expires>$now AND Mode='bot' AND ProcessingId IS NOT NULL AND ProcessingUntil<=$now",("$id",id.ToString()),("$now",Now),("$store",store))==0)return;
        Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Conversation=$id AND State IN('pending','processing')",("$id",id.ToString()));
        Add(db,tx,id,"system","Trợ lý bị gián đoạn. Hội thoại đã chuyển vào hàng chờ nhân viên.");
    }
    private void ExpireZaloWork(SqliteConnection db,SqliteTransaction tx,string connection) {
        Execute(db,tx,"UPDATE CskhChannelControls SET Error='zalo_sync_history_expired' WHERE Connection=$c AND EXISTS(SELECT 1 FROM CskhZaloSync WHERE Connection=$c AND Work IS NOT NULL AND WorkStarted<=$cutoff)",("$c",connection),("$cutoff",Now-7L*86400000));
        Execute(db,tx,"UPDATE CskhZaloSync SET Work=json_object('Started',WorkStarted),Peer=NULL,Pages=0 WHERE Connection=$c AND Work IS NOT NULL AND WorkStarted<=$cutoff AND length(Work)>100",("$c",connection),("$cutoff",Now-7L*86400000));
    }
    private int RecoverOutbox(SqliteConnection db,SqliteTransaction tx,string? connection=null) {
        var clause=connection is null?"":" AND Connection=$connection";
        return ChangeRows(db,tx,"UPDATE CskhChannelOutbox SET State=CASE WHEN State='sending' THEN 'review' ELSE 'accepted' END,Code=CASE WHEN State='sending' THEN 'send_interrupted' ELSE 'check_interrupted' END,Available=$now,Updated=$now WHERE rowid IN(SELECT rowid FROM CskhChannelOutbox WHERE State IN('sending','checking') AND Updated<=$cutoff"+clause+" ORDER BY Updated LIMIT $batch)",("$now",Now),("$cutoff",Now-120000),("$batch",options.CurrentValue.CleanupBatchSize),("$connection",connection));
    }
    public void Purge()=>CleanupBatch();
    public CustomerCleanupResult CleanupBatch()=>Run((db,tx)=>{
        var batch=options.CurrentValue.CleanupBatchSize;var changed=0;var more=false;
        // Constant table names; every selector uses an expiry index and a row limit.
        foreach(var (table,column,cutoff) in new[]{("CskhVisitorSessions","Expires",Now),("CskhRevoked","Expires",Now),("CskhChannelReceipts","Expires",Now),("CskhEvaluationRequests","Expires",Now),("CskhEvaluationRuns","Created",Now-7L*86400000),("CskhZaloCheckpoints","Verified",Now-7L*86400000)}) {
            var rows=ChangeRows(db,tx,$"DELETE FROM {table} WHERE rowid IN(SELECT rowid FROM {table} WHERE {column}<=$cutoff ORDER BY {column} LIMIT $batch)",("$cutoff",cutoff),("$batch",batch));changed+=rows;more|=rows==batch;
        }
        var codes=ChangeRows(db,tx,"UPDATE CskhIdentity SET CodeHash=NULL,ProtectedCode=NULL WHERE Conversation IN(SELECT Conversation FROM CskhIdentity WHERE CodeUntil<=$now AND (CodeHash IS NOT NULL OR ProtectedCode IS NOT NULL) ORDER BY CodeUntil LIMIT $batch)",("$now",Now),("$batch",batch));changed+=codes;more|=codes==batch;
        foreach(var id in Ids(db,tx,"SELECT Id FROM CskhConversations WHERE Mode='bot' AND ProcessingId IS NOT NULL AND ProcessingUntil<=$now AND Expires>$now ORDER BY ProcessingUntil LIMIT $batch",("$now",Now),("$batch",batch))){RecoverConversation(db,tx,id);changed++;more=true;}
        using(var cmd=Command(db,tx,"SELECT Connection FROM CskhZaloSync WHERE Work IS NOT NULL AND WorkStarted<=$cutoff AND length(Work)>100 LIMIT $batch",("$cutoff",Now-7L*86400000),("$batch",Math.Min(batch,50)))) {
            var ids=new List<string>();using(var reader=cmd.ExecuteReader())while(reader.Read())ids.Add(reader.GetString(0));
            foreach(var id in ids){ExpireZaloWork(db,tx,id);changed++;}more|=ids.Count==Math.Min(batch,50);
        }
        var recovered=RecoverOutbox(db,tx);changed+=recovered;more|=recovered==batch;
        var orphaned=ChangeRows(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE rowid IN(SELECT i.rowid FROM CskhChannelInbox i WHERE i.State='processing' AND NOT EXISTS(SELECT 1 FROM CskhConversations c WHERE c.Id=i.Conversation AND c.ProcessingId=i.Id) LIMIT $batch)",("$batch",batch));changed+=orphaned;more|=orphaned==batch;
        // At most one bounded transcript cascade per transaction (messages/requests/media
        // already have per-conversation limits). Do not VACUUM or checkpoint under this lock.
        var conversations=ChangeRows(db,tx,"DELETE FROM CskhConversations WHERE Id IN(SELECT Id FROM CskhConversations WHERE Expires<=$now ORDER BY Expires LIMIT 1)",("$now",Now));changed+=conversations;more|=conversations>0;
        CleanupChanges.Add(changed);return new CustomerCleanupResult(changed,conversations,more);
    });
    public void RunCleanup(CancellationToken ct=default) {
        var started=Stopwatch.GetTimestamp();
        for(var i=0;i<16;i++){ct.ThrowIfCancellationRequested();var result=CleanupBatch();if(!result.More||Stopwatch.GetElapsedTime(started).TotalMilliseconds>=options.CurrentValue.CleanupBudgetMilliseconds)break;}
        var status=StorageStatus();
        StorageBytes.Record(status.Bytes);ReservedVisitors.Record(status.ReservedSessions);
        if(status.Bytes>=options.CurrentValue.StorageWarningBytes||status.ReservedSessions>=status.MaximumSessions*0.8)WarnStorage("capacity_warning");
    }
    public CustomerStorageStatus StorageStatus() {
        var path=Path.GetFullPath(options.CurrentValue.DatabasePath,environment.ContentRootPath);
        var count=RunRead((db,tx)=>Scalar(db,tx,"SELECT Count FROM CskhSessionUsage WHERE Scope=0") is long n?n:
            (long)Scalar(db,tx,"SELECT (SELECT COUNT(*) FROM CskhVisitorSessions)+(SELECT COUNT(*) FROM CskhRevoked r WHERE NOT EXISTS(SELECT 1 FROM CskhVisitorSessions s WHERE s.Store=r.Store AND s.Visitor=r.Visitor))")!);
        long Size(string file)=>File.Exists(file)?new FileInfo(file).Length:0;
        var bytes=Size(path)+Size(path+"-wal");return new(bytes,count,options.CurrentValue.MaximumVisitorSessionsTotal);
    }
}
