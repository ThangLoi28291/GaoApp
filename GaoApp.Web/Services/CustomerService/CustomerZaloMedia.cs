using System.Text.Json;
using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerZaloAttachment(Guid Id,string Kind,string FileName,string ContentType,long Length);
public sealed record CustomerZaloMessage(long Sequence,string Kind,string? SenderName,string? QuotedText,CustomerZaloAttachment[] Attachments,string? SourceTimeText=null);
public sealed record CustomerZaloMediaTarget(string Connection,string Binding,string Peer,long Sequence,CustomerZaloAttachment Attachment);
public sealed record CustomerZaloMediaContent(byte[] Bytes,string ContentType,string FileName,bool Preview);

public static class CustomerZaloMedia
{
    public const int MaximumBytes=30*1024*1024;
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static string Clean(string? value,int limit)=>new((value??"").Where(c=>!char.IsControl(c)).Take(limit).ToArray());
    public static string FileName(string value) {
        var name=Clean(value.Replace('\\','/').Split('/').Last(),180).Trim().Trim('.');
        return string.IsNullOrWhiteSpace(name)?"tep-dinh-kem":name;
    }
    public static bool ImageType(string type)=>type is "image/png" or "image/jpeg" or "image/gif" or "image/webp";
    public static bool ImageSignature(ReadOnlySpan<byte> data,string type)=>type switch {
        "image/png"=>data.StartsWith(new byte[]{137,80,78,71,13,10,26,10}),
        "image/jpeg"=>data.Length>=3&&data[0]==255&&data[1]==216&&data[2]==255,
        "image/gif"=>data.StartsWith("GIF87a"u8)||data.StartsWith("GIF89a"u8),
        "image/webp"=>data.Length>=12&&data[..4].SequenceEqual("RIFF"u8)&&data.Slice(8,4).SequenceEqual("WEBP"u8),
        _=>false
    };
    public static CustomerZaloMessage Parse(JsonElement message,long sequence) {
        var kind=message.GetProperty("kind").GetString()??throw new JsonException();
        if(kind.Length is <1 or >24||!kind.All(char.IsAsciiLetter))throw new JsonException();
        string? Optional(string key,int max)=>message.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.String?Clean(item.GetString(),max):null;
        var files=new List<CustomerZaloAttachment>();
        if(message.TryGetProperty("attachments",out var attachments)&&attachments.ValueKind!=JsonValueKind.Null) {
            if(attachments.ValueKind!=JsonValueKind.Array||attachments.GetArrayLength()>20)throw new JsonException();
            foreach(var a in attachments.EnumerateArray()) {
                var id=a.GetProperty("id").GetGuid();var length=a.GetProperty("length").GetInt64();
                var type=a.GetProperty("contentType").GetString()??"";var assetKind=a.GetProperty("kind").GetString()??"";
                if(id==Guid.Empty||length is <=0 or >1073741824||type.Length is <1 or >128||type.Any(char.IsControl)||assetKind.Length is <1 or >24||!assetKind.All(char.IsAsciiLetter)||files.Any(f=>f.Id==id))throw new JsonException();
                files.Add(new(id,assetKind,FileName(a.GetProperty("fileName").GetString()??""),type.ToLowerInvariant(),length));
            }
        }
        return new(sequence,kind,Optional("senderName",80),Optional("quotedText",1000),files.ToArray(),Optional("sourceTimeText",120));
    }
}

public sealed partial class CustomerServiceStore
{
    private static string ThreadKind(SqliteConnection db,SqliteTransaction tx,Guid id)=>
        Scalar(db,tx,"SELECT Kind FROM CskhChannelThreadDetails WHERE Conversation=$id",("$id",id.ToString())) as string??"Direct";
    private void SaveZaloSource(SqliteConnection db,SqliteTransaction tx,Guid message,CustomerChannelConnection connection,CustomerChannelIncoming input) {
        if(connection.Provider!="zalomarketing"||input.Zalo is null)return;
        Execute(db,tx,"INSERT INTO CskhChannelMessageSources(Message,Connection,ExternalId,Details) VALUES($message,$connection,$external,$details)",
            ("$message",message.ToString()),("$connection",connection.Id),("$external",Hash(input.ExternalId)),("$details",JsonSerializer.Serialize(input.Zalo,CustomerZaloMedia.Json)));
    }
    public bool RefreshZaloMessage(CustomerChannelConnection connection,CustomerChannelIncoming input)=>Run((db,tx)=>{
        if(connection.Provider!="zalomarketing"||input.Zalo is null||!connection.Enabled||ChannelState(db,tx,connection).Paused||!Settings(db,tx,connection.StoreId).Enabled)return false;
        var details=JsonSerializer.Serialize(input.Zalo,CustomerZaloMedia.Json);
        using var cmd=Command(db,tx,"SELECT s.Message,s.Details,m.Conversation FROM CskhChannelMessageSources s JOIN CskhMessages m ON m.Id=s.Message JOIN CskhConversations c ON c.Id=m.Conversation JOIN CskhChannelThreads t ON t.Conversation=c.Id WHERE s.Connection=$connection AND s.ExternalId=$external AND t.Binding=$binding AND t.Peer=$peer AND c.Store=$store AND c.Expires>$now",
            ("$connection",connection.Id),("$external",Hash(input.ExternalId)),("$binding",connection.Binding),("$peer",input.Peer),("$store",connection.StoreId),("$now",Now));
        string message,conversation;using(var r=cmd.ExecuteReader()){if(!r.Read()){r.Close();return SaveZaloHistory(db,tx,connection,input);}
            if(r.GetString(1)==details)return false;message=r.GetString(0);conversation=r.GetString(2);}
        Execute(db,tx,"UPDATE CskhChannelMessageSources SET Details=$details WHERE Message=$message",("$details",details),("$message",message));
        var needsHuman=input.ConversationKind=="Group"||input.Zalo.Kind!="Text"||input.Zalo.Attachments.Length>0;
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1,Mode=CASE WHEN $human=1 AND Mode='bot' THEN 'waiting' ELSE Mode END,Reason=CASE WHEN $human=1 THEN 'channel_needs_human' ELSE Reason END,ProcessingId=CASE WHEN $human=1 THEN NULL ELSE ProcessingId END,ProcessingUntil=CASE WHEN $human=1 THEN NULL ELSE ProcessingUntil END WHERE Id=$id",("$id",conversation),("$human",needsHuman?1:0));
        if(needsHuman)Execute(db,tx,"UPDATE CskhChannelInbox SET State='done' WHERE Conversation=$id AND State IN('pending','processing')",("$id",conversation));
        return true;
    });
    public CustomerZaloMediaTarget StaffZaloMedia(CustomerStaff staff,Guid conversation,Guid message,Guid asset)=>Run((db,tx)=>{
        Read(db,tx,staff.StoreId,conversation,includeMessages:false);
        using var cmd=Command(db,tx,"SELECT t.Connection,t.Binding,t.Peer,s.Details FROM CskhChannelThreads t JOIN CskhMessages m ON m.Conversation=t.Conversation JOIN CskhChannelMessageSources s ON s.Message=m.Id WHERE t.Conversation=$id AND m.Id=$message AND t.Provider='zalomarketing' UNION ALL SELECT t.Connection,t.Binding,t.Peer,h.Details FROM CskhChannelThreads t JOIN CskhChannelHistoryMedia h ON h.Conversation=t.Conversation WHERE t.Conversation=$id AND h.Id=$message AND t.Provider='zalomarketing' LIMIT 1",
            ("$id",conversation.ToString()),("$message",message.ToString()));
        using var r=cmd.ExecuteReader();if(!r.Read())throw Missing();
        var details=JsonSerializer.Deserialize<CustomerZaloMessage>(r.GetString(3),CustomerZaloMedia.Json)??throw Missing();
        var attachment=details.Attachments.SingleOrDefault(a=>a.Id==asset)??throw Missing();
        return new CustomerZaloMediaTarget(r.GetString(0),r.GetString(1),r.GetString(2),details.Sequence,attachment);
    });
}
