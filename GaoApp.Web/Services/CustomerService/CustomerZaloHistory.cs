using System.Text.Json;
using Microsoft.Data.Sqlite;
namespace GaoApp.Web.Services.CustomerService;

// Older imports do not contain a source-message mapping. Keep their media in a clearly
// labelled source-history gallery instead of guessing which old text bubble owns an asset.
public sealed record CustomerZaloHistoryItem(Guid Id,DateTimeOffset ObservedAt,CustomerZaloMessage Zalo,bool IsOutgoing=false);
public sealed partial class CustomerServiceStore
{
    private bool SaveZaloHistory(SqliteConnection db,SqliteTransaction tx,CustomerChannelConnection connection,CustomerChannelIncoming input,bool outgoing=false) {
        if(input.Zalo is null||input.Zalo.Attachments.Length==0||input.SentAt<clock.GetUtcNow().AddDays(-7)||input.SentAt>clock.GetUtcNow().AddMinutes(5))return false;
        var conversation=Scalar(db,tx,"SELECT c.Id FROM CskhChannelThreads t JOIN CskhConversations c ON c.Id=t.Conversation WHERE t.Connection=$connection AND t.Binding=$binding AND t.Peer=$peer AND c.Store=$store AND c.Expires>$now ORDER BY CASE WHEN c.Mode='closed' THEN 1 ELSE 0 END,c.Created DESC LIMIT 1",
            ("$connection",connection.Id),("$binding",connection.Binding),("$peer",input.Peer),("$store",connection.StoreId),("$now",Now)) as string;
        if(conversation is null)return false;
        var external=Hash(input.ExternalId);var details=JsonSerializer.Serialize(input.Zalo,CustomerZaloMedia.Json);
        using(var cmd=Command(db,tx,"SELECT Details,Conversation FROM CskhChannelHistoryMedia WHERE Connection=$connection AND ExternalId=$external",("$connection",connection.Id),("$external",external)))
        using(var r=cmd.ExecuteReader()){if(r.Read()){if(r.GetString(0)==details)return false;conversation=r.GetString(1);}}
        Execute(db,tx,"INSERT INTO CskhChannelHistoryMedia(Id,Conversation,Connection,ExternalId,Observed,Outgoing,Details) VALUES($id,$conversation,$connection,$external,$observed,$outgoing,$details) ON CONFLICT(Connection,ExternalId) DO UPDATE SET Details=excluded.Details",
            ("$id",Guid.NewGuid().ToString()),("$conversation",conversation),("$connection",connection.Id),("$external",external),("$observed",input.SentAt.ToUnixTimeMilliseconds()),("$outgoing",outgoing?1:0),("$details",details));
        Execute(db,tx,"DELETE FROM CskhChannelHistoryMedia WHERE Id IN(SELECT Id FROM CskhChannelHistoryMedia WHERE Conversation=$id ORDER BY Observed DESC,Id DESC LIMIT -1 OFFSET 50)",("$id",conversation));
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1 WHERE Id=$id",("$id",conversation));return true;
    }
    public bool SyncZaloHistory(CustomerChannelConnection connection,CustomerChannelIncoming input,bool outgoing=false)=>Run((db,tx)=>{
        if(connection.Provider!="zalomarketing"||!connection.Enabled||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled)return false;
        if(input.SentAt<clock.GetUtcNow().AddDays(-7)||input.SentAt>clock.GetUtcNow().AddMinutes(5))return false;
        return SaveZaloHistory(db,tx,connection,input,outgoing);
    });
    private static CustomerZaloHistoryItem[] ReadZaloHistory(SqliteConnection db,SqliteTransaction tx,Guid conversation) {
        using var cmd=Command(db,tx,"SELECT Id,Observed,Details,Outgoing FROM CskhChannelHistoryMedia WHERE Conversation=$id ORDER BY Observed,Id LIMIT 50",("$id",conversation.ToString()));
        using var r=cmd.ExecuteReader();var rows=new List<CustomerZaloHistoryItem>();
        while(r.Read())rows.Add(new(Guid.Parse(r.GetString(0)),DateTimeOffset.FromUnixTimeMilliseconds(r.GetInt64(1)),JsonSerializer.Deserialize<CustomerZaloMessage>(r.GetString(2),CustomerZaloMedia.Json)!,r.GetInt32(3)==1));return rows.ToArray();
    }
}
