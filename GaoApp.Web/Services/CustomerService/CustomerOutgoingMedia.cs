using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using GaoApp.Web.Services.Assistant;
using Microsoft.Data.Sqlite;

namespace GaoApp.Web.Services.CustomerService;

public sealed record CustomerOutgoingAttachment(string Kind,string FileName,string ContentType,long Length);
public sealed record CustomerOutgoingAsset(CustomerOutgoingAttachment Attachment,string Sha256,[property:JsonIgnore] byte[] Bytes);
public sealed class CustomerMediaReply
{
    public Guid RequestId {get;set;}
    public long Version {get;set;}
    public string? Caption {get;set;}
    public IFormFile? Asset {get;set;}
}
public static class CustomerOutgoingMedia
{
    public const int MaximumImageBytes=10*1024*1024,MaximumFileBytes=20*1024*1024;
    public static async Task<CustomerOutgoingAsset> Inspect(IFormFile file,CancellationToken ct)
    {
        var name=file.FileName.Trim().Normalize(NormalizationForm.FormC);
        if(name.Length is <1 or >120||Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw Invalid();
        var image=Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png";
        var max=image?MaximumImageBytes:MaximumFileBytes;
        if(file.Length is <=0||file.Length>max)throw Invalid();
        await using var input=file.OpenReadStream();using var data=new MemoryStream();var chunk=new byte[81920];
        while(true){var count=await input.ReadAsync(chunk,ct);if(count==0)break;if(data.Length+count>max)throw Invalid();await data.WriteAsync(chunk.AsMemory(0,count),ct);}
        var bytes=data.ToArray();if(bytes.Length!=file.Length)throw Invalid();
        var type=Detect(name,bytes);return new(new(image?"Image":"File",name,type,bytes.Length),Convert.ToHexString(SHA256.HashData(bytes)),bytes);
    }
    public static void Validate(CustomerOutgoingAsset asset)
    {
        var a=asset.Attachment;
        if(a.Length!=asset.Bytes.Length||a.Length<=0||a.Length>(a.Kind=="Image"?MaximumImageBytes:MaximumFileBytes)||
            a.Kind is not ("Image" or "File")||a.FileName.Length is <1 or >120||Path.GetFileName(a.FileName)!=a.FileName||
            a.FileName.IndexOfAny(Path.GetInvalidFileNameChars())>=0||a.FileName!=a.FileName.Trim().Normalize(NormalizationForm.FormC)||
            Detect(a.FileName,asset.Bytes)!=a.ContentType||(a.Kind=="Image")!=a.ContentType.StartsWith("image/",StringComparison.Ordinal)||
            Convert.ToHexString(SHA256.HashData(asset.Bytes))!=asset.Sha256)throw Invalid();
    }
    private static string Detect(string name,byte[] bytes)
    {
        var ext=Path.GetExtension(name).ToLowerInvariant();var data=bytes.AsSpan();
        if(ext==".png"&&CustomerZaloMedia.ImageSignature(data,"image/png"))return "image/png";
        if(ext is ".jpg" or ".jpeg"&&CustomerZaloMedia.ImageSignature(data,"image/jpeg"))return "image/jpeg";
        if(ext==".pdf"&&data.StartsWith("%PDF-"u8))return "application/pdf";
        if(ext is ".doc" or ".xls"&&data.StartsWith(new byte[]{208,207,17,224,161,177,26,225})&&
            (ext==".doc"?data.IndexOf(Encoding.Unicode.GetBytes("WordDocument"))>=0:data.IndexOf(Encoding.Unicode.GetBytes("Workbook"))>=0||data.IndexOf(Encoding.Unicode.GetBytes("Book"))>=0))
            return ext==".doc"?"application/msword":"application/vnd.ms-excel";
        if(ext is ".docx" or ".xlsx")try{
            using var zip=new ZipArchive(new MemoryStream(bytes),ZipArchiveMode.Read);
            if(zip.GetEntry("[Content_Types].xml") is not null&&zip.Entries.Any(e=>e.FullName.StartsWith(ext==".docx"?"word/":"xl/",StringComparison.Ordinal)))
                return ext==".docx"?"application/vnd.openxmlformats-officedocument.wordprocessingml.document":"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        }catch(InvalidDataException){throw Invalid();}
        throw Invalid();
    }
    private static AssistantException Invalid()=>new(422,"customer_media_invalid","Chọn ảnh JPG/PNG tối đa 10 MiB hoặc PDF, Word, Excel tối đa 20 MiB. Tên tệp tối đa 120 ký tự; nội dung phải đúng định dạng.");
}

public sealed partial class CustomerServiceStore
{
    private static CustomerOutgoingAsset? ReadOutgoingAsset(SqliteConnection db,SqliteTransaction tx,Guid message)
    {
        using var cmd=Command(db,tx,"SELECT Kind,FileName,ContentType,Length,Sha256,Bytes FROM CskhOutgoingAssets WHERE Message=$message",("$message",message.ToString()));
        using var r=cmd.ExecuteReader();return r.Read()?new(new(r.GetString(0),r.GetString(1),r.GetString(2),r.GetInt64(3)),r.GetString(4),(byte[])r[5]):null;
    }
    public CustomerOutgoingAsset StaffOutgoingMedia(CustomerStaff staff,Guid id,Guid message)=>Run((db,tx)=>{
        var c=Read(db,tx,staff.StoreId,id);
        if(!c.Messages.Any(m=>m.Id==message&&m.Role=="agent"&&m.Outgoing is not null))throw Missing();
        return ReadOutgoingAsset(db,tx,message)??throw Missing();
    });
    public CustomerConversation StaffMediaReply(CustomerStaff staff,Guid id,CustomerMediaReply input,CustomerOutgoingAsset asset,CustomerChannelConnection connection)=>Run((db,tx)=>{
        if(!staff.CanReply)throw AssistantException.Denied();
        var c=Read(db,tx,staff.StoreId,id);CustomerOutgoingMedia.Validate(asset);var a=asset.Attachment;
        var text=input.Caption?.Trim()??"";
        if(text.Length>8000||text.EnumerateRunes().Count()>4000)throw AssistantException.Invalid();
        if(Replay(db,tx,id,input.RequestId,"agent-media:"+staff.UserId,new{input.Version,text,a.Kind,a.FileName,a.ContentType,a.Length,asset.Sha256}))return c;
        if(c.Version!=input.Version||c.Mode!="human"||c.Owner!=staff.UserId)throw Conflict();
        if(c.Source!="zalomarketing"||connection.Provider!="zalomarketing"||connection.StoreId!=staff.StoreId||!connection.Enabled||ChannelState(db,tx,connection).Paused||!Settings(db,tx,staff.StoreId).Enabled)
            throw new AssistantException(409,"customer_media_channel_unavailable","Kênh Zalo chưa sẵn sàng gửi tệp.");
        if((long)Scalar(db,tx,"SELECT COUNT(*) FROM CskhChannelThreads WHERE Conversation=$id AND Connection=$connection AND Binding=$binding",("$id",id.ToString()),("$connection",connection.Id),("$binding",connection.Binding))! !=1)throw Conflict();
        var used=(long)Scalar(db,tx,"SELECT COALESCE(SUM(a.Length),0) FROM CskhOutgoingAssets a JOIN CskhMessages m ON m.Id=a.Message JOIN CskhConversations c ON c.Id=m.Conversation WHERE c.Store=$store",("$store",staff.StoreId))!;
        var total=(long)Scalar(db,tx,"SELECT COALESCE(SUM(Length),0) FROM CskhOutgoingAssets")!;
        if(used+a.Length>options.CurrentValue.MaximumOutgoingMediaBytesPerStore||total+a.Length>options.CurrentValue.MaximumOutgoingMediaBytesTotal)
            throw new AssistantException(429,"customer_media_storage_full","Dung lượng tệp CSKH đã đầy. Liên hệ quản trị viên hoặc chờ hết hạn lưu hội thoại.");
        var message=Add(db,tx,id,"agent",text,staff.Name,staff.UserId);
        Execute(db,tx,"INSERT INTO CskhOutgoingAssets VALUES($message,$kind,$name,$type,$length,$sha,$bytes)",("$message",message.ToString()),("$kind",a.Kind),("$name",a.FileName),("$type",a.ContentType),("$length",a.Length),("$sha",asset.Sha256),("$bytes",asset.Bytes));
        Execute(db,tx,"UPDATE CskhConversations SET Version=Version+1,Updated=$now,Unread=0 WHERE Id=$id",("$id",id.ToString()),("$now",Now));
        return Read(db,tx,staff.StoreId,id);
    });
    public string StaffMediaConnection(CustomerStaff staff,Guid id)=>Run((db,tx)=>{
        var c=Read(db,tx,staff.StoreId,id);if(c.Source!="zalomarketing")throw Conflict();
        return Scalar(db,tx,"SELECT Connection FROM CskhChannelThreads WHERE Conversation=$id",("$id",id.ToString())) as string??throw Missing();
    });
}
