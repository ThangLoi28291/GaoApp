using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;
namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerServiceStore
{
    public static bool RatingKind(string? kind)=>kind is null or "helpful" or "wrong_product" or "wrong_price" or "unclear" or "wrong_policy" or "other";
    public static bool QualitySource(string source)=>source is "all" or "web" or "facebook" or "zalomarketing";
    private static void QualityManager(CustomerStaff who){if(who.UserId<=0||who.StoreId<=0||!who.CanManage)throw AssistantException.Denied();}
    private static string Actor(Guid visitor)=>"visitor:"+visitor;
    private static string Actor(CustomerStaff who)=>"staff:"+who.UserId;
    private CustomerVote[] Votes(SqliteConnection db,SqliteTransaction tx,Guid conversation,string actor){
        using var cmd=Command(db,tx,"SELECT Message,Kind FROM CskhQualityFeedback WHERE Conversation=$id AND Actor=$actor ORDER BY Created,Id",("$id",conversation.ToString()),("$actor",actor));using var r=cmd.ExecuteReader();var result=new List<CustomerVote>();while(r.Read())result.Add(new(Guid.Parse(r.GetString(0)),r.GetString(1)));return result.ToArray();
    }
    public CustomerVote[] CustomerVotes(int store,Guid visitor,Guid conversation)=>Run((db,tx)=>{
        RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var c=Read(db,tx,store,conversation,visitor);if(c.Source!="web")throw Missing();return Votes(db,tx,conversation,Actor(visitor));
    });
    public CustomerVote[] StaffVotes(CustomerStaff who,Guid conversation)=>Run((db,tx)=>{Read(db,tx,who.StoreId,conversation);return Votes(db,tx,conversation,Actor(who));});
    private CustomerVote[] Vote(SqliteConnection db,SqliteTransaction tx,CustomerConversation conversation,string actor,CustomerRating input){
        if(input.MessageId==Guid.Empty||!RatingKind(input.Kind))throw AssistantException.Invalid();
        if(!conversation.Messages.Any(m=>m.Id==input.MessageId&&m.Role=="assistant"))throw new AssistantException(404,"customer_answer_not_found","Không tìm thấy câu trả lời AI trong hội thoại được phép xem.");
        if(Replay(db,tx,conversation.Id,input.RequestId,"quality:"+actor,new{input.MessageId,input.Kind}))return Votes(db,tx,conversation.Id,actor);
        if(input.Kind is null)Execute(db,tx,"DELETE FROM CskhQualityFeedback WHERE Message=$message AND Actor=$actor",("$message",input.MessageId.ToString()),("$actor",actor));
        else Execute(db,tx,"""
            INSERT INTO CskhQualityFeedback VALUES($id,$conversation,$message,$actor,$kind,$status,$version,$now,$now,NULL,NULL)
            ON CONFLICT(Message,Actor) DO UPDATE SET Kind=$kind,Status=$status,Version=$version,Updated=$now,Resolution=NULL,ReviewedBy=NULL
            WHERE Kind!=$kind
            """,("$id",Guid.NewGuid().ToString()),("$conversation",conversation.Id.ToString()),("$message",input.MessageId.ToString()),("$actor",actor),("$kind",input.Kind),("$status",input.Kind=="helpful"?"resolved":"open"),("$version",Guid.NewGuid().ToString("N")),("$now",Now));
        return Votes(db,tx,conversation.Id,actor);
    }
    public CustomerVote[] RateCustomer(int store,Guid visitor,Guid conversation,CustomerRating input)=>Run((db,tx)=>{
        RequireVisitor(db,tx,store,visitor);RequireEnabled(db,tx,store);var c=Read(db,tx,store,conversation,visitor);if(c.Source!="web")throw Missing();return Vote(db,tx,c,Actor(visitor),input);
    });
    public CustomerVote[] RateStaff(CustomerStaff who,Guid conversation,CustomerRating input)=>Run((db,tx)=>{
        if(!who.CanReply||who.UserId<=0)throw AssistantException.Denied();return Vote(db,tx,Read(db,tx,who.StoreId,conversation),Actor(who),input);
    });
    private static CustomerQualityCount[] Counts(SqliteConnection db,SqliteTransaction tx,string sql,params (string,object?)[] args){
        using var cmd=Command(db,tx,sql,args);using var r=cmd.ExecuteReader();var rows=new List<CustomerQualityCount>();while(r.Read())rows.Add(new(r.GetString(0),r.GetInt64(1)));return rows.ToArray();
    }
    public CustomerQualityReport QualityReport(CustomerStaff who,int days,string source)=>RunRead<CustomerQualityReport>((db,tx)=>{
        QualityManager(who);if(days is not (1 or 7)||!QualitySource(source))throw AssistantException.Invalid();
        var retention=options.CurrentValue.RetentionDays;var from=Now-Math.Min(days,retention)*86400000L;
        const string join=" FROM CskhConversations c LEFT JOIN CskhChannelThreads t ON t.Conversation=c.Id ";
        const string scope=" WHERE c.Store=$store AND c.Expires>$now AND ($source='all' OR COALESCE(t.Provider,'web')=$source) ";
        (string,object?)[] args=[("$store",who.StoreId),("$source",source),("$from",from),("$now",Now)];
        var modes=Counts(db,tx,"SELECT c.Mode,COUNT(*)"+join+scope+"AND c.Created>=$from GROUP BY c.Mode",args);
        var sources=Counts(db,tx,"SELECT COALESCE(t.Provider,'web'),COUNT(*)"+join+scope+"AND c.Created>=$from GROUP BY COALESCE(t.Provider,'web')",args);
        var messages=Counts(db,tx,"SELECT m.Role,COUNT(*)"+join+"JOIN CskhMessages m ON m.Conversation=c.Id"+scope+"AND m.Created>=$from GROUP BY m.Role",args);
        var reasons=Counts(db,tx,"SELECT c.Reason,COUNT(*)"+join+scope+"AND c.Updated>=$from AND c.Reason IS NOT NULL GROUP BY c.Reason ORDER BY COUNT(*) DESC LIMIT 20",args);
        var deliveries=Counts(db,tx,"SELECT o.State,COUNT(*)"+join+"JOIN CskhChannelOutbox o ON o.Conversation=c.Id"+scope+"AND o.Created>=$from GROUP BY o.State",args);
        var votes=Counts(db,tx,"SELECT CASE WHEN f.Actor LIKE 'visitor:%' THEN 'customer' ELSE 'staff' END||':'||f.Kind,COUNT(*)"+join+"JOIN CskhQualityFeedback f ON f.Conversation=c.Id"+scope+"AND f.Updated>=$from GROUP BY 1",args);
        var customer=votes.Where(v=>v.Key.StartsWith("customer:",StringComparison.Ordinal)).Sum(v=>v.Count);var helpful=votes.Where(v=>v.Key=="customer:helpful").Sum(v=>v.Count);
        var open=(long)Scalar(db,tx,"SELECT COUNT(*)"+join+"JOIN CskhQualityFeedback f ON f.Conversation=c.Id"+scope+"AND f.Updated>=$from AND f.Status='open'",args)!;
        return new(DateTimeOffset.FromUnixTimeMilliseconds(Now),DateTimeOffset.FromUnixTimeMilliseconds(from),retention,modes.Sum(m=>m.Count),modes,sources,messages,reasons,deliveries,customer,helpful,votes.Where(v=>v.Key.StartsWith("staff:",StringComparison.Ordinal)).Sum(v=>v.Count),open,customer==0?null:Math.Round((double)helpful/customer,4));
    });
    private CustomerReviewItem ReviewItem(SqliteConnection db,SqliteTransaction tx,int store,Guid id){
        using var cmd=Command(db,tx,"""
            SELECT f.Conversation,f.Message,COALESCE(t.Provider,'web'),f.Actor,f.Kind,f.Status,f.Version,f.Created,f.Updated,f.Resolution,f.ReviewedBy,m.Text,m.rowid
            FROM CskhQualityFeedback f JOIN CskhConversations c ON c.Id=f.Conversation JOIN CskhMessages m ON m.Id=f.Message
            LEFT JOIN CskhChannelThreads t ON t.Conversation=c.Id WHERE f.Id=$id AND c.Store=$store AND c.Expires>$now
            """,("$id",id.ToString()),("$store",store),("$now",Now));
        CustomerReviewItem row;long ordinal;
        using(var r=cmd.ExecuteReader()){if(!r.Read())throw Missing();ordinal=r.GetInt64(12);row=new(id,Guid.Parse(r.GetString(0)),Guid.Parse(r.GetString(1)),r.GetString(2),r.GetString(3).StartsWith("visitor:",StringComparison.Ordinal)?"customer":"staff",r.GetString(4),r.GetString(5),r.GetString(6),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(7)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(8)),r.IsDBNull(9)?null:r.GetString(9),r.IsDBNull(10)?null:r.GetInt32(10),"",r.GetString(11));}
        var question=Scalar(db,tx,"SELECT Text FROM CskhMessages WHERE Conversation=$conversation AND Role='customer' AND rowid<$ordinal ORDER BY rowid DESC LIMIT 1",("$conversation",row.ConversationId.ToString()),("$ordinal",ordinal)) as string??"";
        return row with{Question=question};
    }
    public CustomerReviewItem QualityItem(CustomerStaff who,Guid id)=>RunRead((db,tx)=>{QualityManager(who);return ReviewItem(db,tx,who.StoreId,id);});
    public CustomerReviewPage QualityQueue(CustomerStaff who,int days,string source,string status,int page=0)=>RunRead<CustomerReviewPage>((db,tx)=>{
        QualityManager(who);if(days is not (1 or 7)||!QualitySource(source)||status is not ("all" or "open" or "resolved")||page is <0 or >200)throw AssistantException.Invalid();
        var ids=Ids(db,tx,"""
            SELECT f.Id FROM CskhQualityFeedback f JOIN CskhConversations c ON c.Id=f.Conversation LEFT JOIN CskhChannelThreads t ON t.Conversation=c.Id
            WHERE c.Store=$store AND c.Expires>$now AND f.Updated>=$from AND ($source='all' OR COALESCE(t.Provider,'web')=$source) AND ($status='all' OR f.Status=$status)
            ORDER BY f.Updated DESC,f.Id LIMIT 51 OFFSET $offset
            """,("$store",who.StoreId),("$from",Now-Math.Min(days,options.CurrentValue.RetentionDays)*86400000L),("$source",source),("$status",status),("$offset",page*50),("$now",Now));
        return new(ids.Take(50).Select(id=>ReviewItem(db,tx,who.StoreId,id)).ToArray(),ids.Length>50,page);
    });
    public CustomerReviewItem ReviewQuality(CustomerStaff who,Guid id,CustomerQualityReview input)=>Run((db,tx)=>{
        QualityManager(who);if(!Guid.TryParseExact(input.Version,"N",out _)||input.Action is not ("resolve" or "reopen")
            ||input.Action=="resolve"&&input.Resolution is not ("product_data" or "pricing_source" or "policy_source" or "language" or "not_issue" or "other")
            ||input.Action=="reopen"&&input.Resolution is not null)throw AssistantException.Invalid();
        var row=ReviewItem(db,tx,who.StoreId,id);
        if(Replay(db,tx,row.ConversationId,input.RequestId,"quality-review:"+who.UserId,new{id,input.Version,input.Action,input.Resolution}))return row;
        if(row.Version!=input.Version)throw Conflict();
        Execute(db,tx,"UPDATE CskhQualityFeedback SET Status=$status,Version=$version,Resolution=$resolution,ReviewedBy=$who,Updated=$now WHERE Id=$id",("$status",input.Action=="resolve"?"resolved":"open"),("$version",Guid.NewGuid().ToString("N")),("$resolution",input.Resolution),("$who",who.UserId),("$now",Now),("$id",id.ToString()));
        return ReviewItem(db,tx,who.StoreId,id);
    });
}
