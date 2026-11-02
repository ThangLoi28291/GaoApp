using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerServiceStore
{
    private bool IsRevoked(SqliteConnection db,SqliteTransaction tx,int store,Guid visitor)=>
        Scalar(db,tx,"SELECT 1 FROM CskhRevoked WHERE Store=$store AND Visitor=$visitor AND Expires>$now",("$store",store),("$visitor",visitor.ToString()),("$now",Now)) is not null||
        Scalar(db,tx,"SELECT 1 FROM CskhVisitorSessions WHERE Store=$store AND Visitor=$visitor AND Revoked=1 AND Expires>$now",("$store",store),("$visitor",visitor.ToString()),("$now",Now)) is not null;
    public bool Revoked(int store,Guid visitor)=>Run((db,tx)=>IsRevoked(db,tx,store,visitor),readOnly:true);
    private void RequireVisitor(SqliteConnection db,SqliteTransaction tx,int store,Guid visitor) {
        Valid(store,visitor);
        if(!VisitorAllowed(db,tx,store,visitor))throw SessionExpired();
    }
    private bool VisitorAllowed(SqliteConnection db,SqliteTransaction tx,int store,Guid visitor)=>!IsRevoked(db,tx,store,visitor)&&
        Scalar(db,tx,"SELECT 1 FROM CskhVisitorSessions WHERE Store=$store AND Visitor=$visitor AND Expires<=$now",("$store",store),("$visitor",visitor.ToString()),("$now",Now)) is null&&
        Scalar(db,tx,"SELECT 1 FROM CskhConversations c JOIN CskhConversationSessions s ON s.Conversation=c.Id WHERE c.Store=$store AND c.Visitor=$visitor AND s.Expires<=$now LIMIT 1",("$store",store),("$visitor",visitor.ToString()),("$now",Now)) is null;
    private static AssistantException SessionExpired()=>new(401,"customer_session_expired","Phiên hỗ trợ đã hết hạn. Mình mở lại trang để bắt đầu nhé.");
    private void TicketValid(CustomerTicket ticket) {
        Valid(ticket.StoreId,ticket.VisitorId);if(ticket.Version is not (1 or 2)||ticket.ExpiresAt<=clock.GetUtcNow()||ticket.ExpiresAt>clock.GetUtcNow().AddDays(7))throw SessionExpired();
    }
    private bool? KnownVisitor(SqliteConnection db,SqliteTransaction tx,CustomerTicket ticket) {
        if(IsRevoked(db,tx,ticket.StoreId,ticket.VisitorId))return false;
        var expiry=Scalar(db,tx,"SELECT Expires FROM CskhVisitorSessions WHERE Store=$store AND Visitor=$visitor",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()));
        return expiry is long at?at>=ticket.ExpiresAt.ToUnixTimeMilliseconds():null;
    }
    // Registered active and ended sessions both occupy a slot until cookie expiry.
    // Logout only changes its reserved slot, so a full store can still revoke it.
    public bool AcceptVisitorSession(CustomerTicket ticket) {
        TicketValid(ticket);var known=Run((db,tx)=>KnownVisitor(db,tx,ticket),readOnly:true);if(known is {} valid)return valid;
        // New-format cookies must already have a reservation. Only genuine old-format
        // cookies can migrate, within the maximum lifetime of a pre-upgrade cookie.
        if(ticket.Version!=1)return false;
        PrepareVisitorUsage(ticket.StoreId);
        return Run((db,tx)=>{var current=KnownVisitor(db,tx,ticket);if(current is {} existing)return existing;
            if(Scalar(db,tx,"SELECT LegacyUntil FROM CskhSessionUsage WHERE Scope=0") is long until&&until<=Now)return false;
            ReserveVisitor(db,tx,ticket);return true;});
    }
    public void RegisterVisitorSession(CustomerTicket ticket) {
        TicketValid(ticket);PrepareVisitorUsage(ticket.StoreId);Run((db,tx)=>{if(KnownVisitor(db,tx,ticket) is {} known){if(!known)throw SessionExpired();return true;}ReserveVisitor(db,tx,ticket);return true;});
    }
    private void PrepareVisitorUsage(int store) {
        if(RunRead((db,tx)=>Scalar(db,tx,"SELECT COUNT(*) FROM CskhSessionUsage WHERE Scope IN(0,$store)",("$store",store)) is long count&&count==2))return;
        // Commit legacy accounting once even when the following admission is refused.
        // Otherwise an already oversized legacy database would be recounted on every 429.
        Run((db,tx)=>{EnsureVisitorUsage(db,tx,store);return true;});
    }
    private void EnsureVisitorUsage(SqliteConnection db,SqliteTransaction tx,int store) {
        if(Scalar(db,tx,"SELECT 1 FROM CskhSessionUsage WHERE Scope=0") is null)
            Execute(db,tx,"INSERT INTO CskhSessionUsage SELECT 0,(SELECT COUNT(*) FROM CskhVisitorSessions)+(SELECT COUNT(*) FROM CskhRevoked r WHERE NOT EXISTS(SELECT 1 FROM CskhVisitorSessions s WHERE s.Store=r.Store AND s.Visitor=r.Visitor)),$legacyUntil",("$legacyUntil",Now+7L*86400000));
        if(Scalar(db,tx,"SELECT 1 FROM CskhSessionUsage WHERE Scope=$store",("$store",store)) is null)
            Execute(db,tx,"INSERT INTO CskhSessionUsage SELECT $store,(SELECT COUNT(*) FROM CskhVisitorSessions WHERE Store=$store)+(SELECT COUNT(*) FROM CskhRevoked r WHERE r.Store=$store AND NOT EXISTS(SELECT 1 FROM CskhVisitorSessions s WHERE s.Store=r.Store AND s.Visitor=r.Visitor)),NULL",("$store",store));
    }
    private void ReserveVisitor(SqliteConnection db,SqliteTransaction tx,CustomerTicket ticket) {
        var settings=options.CurrentValue;
        // A small indexed reclaim is confined to new admission, never to ordinary reads.
        Execute(db,tx,"DELETE FROM CskhVisitorSessions WHERE rowid IN(SELECT rowid FROM CskhVisitorSessions WHERE Expires<=$now ORDER BY Expires LIMIT $batch)",("$now",Now),("$batch",settings.CleanupBatchSize));
        Execute(db,tx,"DELETE FROM CskhRevoked WHERE rowid IN(SELECT rowid FROM CskhRevoked WHERE Expires<=$now ORDER BY Expires LIMIT $batch)",("$now",Now),("$batch",settings.CleanupBatchSize));
        EnsureVisitorUsage(db,tx,ticket.StoreId);
        var storeCount=(long)Scalar(db,tx,"SELECT Count FROM CskhSessionUsage WHERE Scope=$store",("$store",ticket.StoreId))!;
        var total=(long)Scalar(db,tx,"SELECT Count FROM CskhSessionUsage WHERE Scope=0")!;
        if(storeCount>=settings.MaximumVisitorSessionsPerStore*0.8)WarnStorage("visitor_store_capacity_warning");
        if(storeCount>=settings.MaximumVisitorSessionsPerStore||total>=settings.MaximumVisitorSessionsTotal)throw VisitorLimit("capacity");
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhVisitorSessions WHERE Store=$store AND Issued>$from",("$store",ticket.StoreId),("$from",Now-60000))!>=settings.VisitorSessionsPerMinutePerStore||
            (long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhVisitorSessions WHERE Issued>$from",("$from",Now-60000))!>=settings.VisitorSessionsPerMinuteTotal)throw VisitorLimit("rate");
        Execute(db,tx,"INSERT INTO CskhVisitorSessions(Store,Visitor,Expires,Issued) VALUES($store,$visitor,$expires,$now)",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()),("$expires",ticket.ExpiresAt.ToUnixTimeMilliseconds()),("$now",Now));
        Execute(db,tx,"INSERT INTO CskhConversationSessions SELECT c.Id,$expires FROM CskhConversations c WHERE c.Store=$store AND c.Visitor=$visitor AND c.Expires>$now AND NOT EXISTS(SELECT 1 FROM CskhChannelThreads t WHERE t.Conversation=c.Id) AND NOT EXISTS(SELECT 1 FROM CskhConversationSessions s WHERE s.Conversation=c.Id)",("$expires",ticket.ExpiresAt.ToUnixTimeMilliseconds()),("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()),("$now",Now));
    }
    private AssistantException VisitorLimit(string scope) {
        VisitorRejected(scope);
        return new(429,scope=="rate"?"customer_session_rate_limit":"customer_session_capacity","Chat đang có nhiều phiên truy cập. Vui lòng thử mở phiên mới sau ít phút; phiên đang sử dụng vẫn được giữ.");
    }
    public void Revoke(CustomerTicket ticket) {
        TicketValid(ticket);PrepareVisitorUsage(ticket.StoreId);Run((db,tx)=>{
            if(Scalar(db,tx,"SELECT 1 FROM CskhVisitorSessions WHERE Store=$store AND Visitor=$visitor",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString())) is null&&
                Scalar(db,tx,"SELECT 1 FROM CskhRevoked WHERE Store=$store AND Visitor=$visitor",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString())) is null)ReserveVisitor(db,tx,ticket);
            Execute(db,tx,"UPDATE CskhVisitorSessions SET Revoked=1,Expires=MAX(Expires,$expires) WHERE Store=$store AND Visitor=$visitor",("$expires",ticket.ExpiresAt.ToUnixTimeMilliseconds()),("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()));
            Execute(db,tx,"INSERT INTO CskhRevoked VALUES($store,$visitor,$expires) ON CONFLICT(Store,Visitor) DO UPDATE SET Expires=MAX(Expires,excluded.Expires)",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()),("$expires",ticket.ExpiresAt.ToUnixTimeMilliseconds()));
            Execute(db,tx,"UPDATE CskhConversations SET Mode='closed',Reason='visitor_session_ended',Owner=NULL,OwnerName=NULL,Version=Version+1,Updated=$now,ProcessingId=NULL,ProcessingUntil=NULL WHERE Store=$store AND Visitor=$visitor AND Mode!='closed'",("$store",ticket.StoreId),("$visitor",ticket.VisitorId.ToString()),("$now",Now));return true;
        });
    }
}
