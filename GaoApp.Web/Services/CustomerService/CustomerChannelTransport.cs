using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerChannelTransport(HttpClient http,CustomerChannelRegistry registry,TimeProvider clock)
{
    private async Task<JsonElement> Json(HttpRequestMessage request,CancellationToken ct,Guid? expectedProfile=null,bool media=false) {
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(TimeSpan.FromSeconds(media?45:15));ct=budget.Token;
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,ct);
        if(!response.IsSuccessStatusCode)throw new ChannelHttpException((int)response.StatusCode);
        if(expectedProfile is Guid profile&&(!response.Headers.TryGetValues("X-ZaloMarketing-Sender-Profile-Id",out var evidence)||evidence.Count()!=1||!Guid.TryParseExact(evidence.Single(),"D",out var actual)||actual!=profile))throw new JsonException("Sender profile evidence mismatch.");
        await response.Content.LoadIntoBufferAsync(1024*1024).WaitAsync(ct);using var data=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));AssistantJwt.Unique(data.RootElement);return data.RootElement.Clone();
    }
    public async Task<JsonElement> ZaloGet(CustomerChannelConnection c,string path,CancellationToken ct) {
        using var req=new HttpRequestMessage(HttpMethod.Get,new Uri(registry.ZaloOrigin(c),path));req.Headers.Authorization=new("Bearer",registry.Secret(c.AccessTokenFile));return await Json(req,ct,path.StartsWith("api/v1/inbox/",StringComparison.Ordinal)?Guid.Parse(c.AccountId):null);
    }
    public async Task<CustomerChannelResult> Dispatch(CustomerChannelConnection c,CustomerChannelDispatch item,CancellationToken ct) {
        try{
            if(c.Provider=="facebook") {
                using var req=new HttpRequestMessage(HttpMethod.Post,$"https://graph.facebook.com/{c.GraphVersion}/{c.AccountId}/messages");req.Headers.Authorization=new("Bearer",registry.Secret(c.AccessTokenFile));
                req.Content=JsonContent.Create(new{recipient=new{id=item.Peer},messaging_type="RESPONSE",message=new{text=item.Text}});
                var body=await Json(req,ct);if(body.GetProperty("recipient_id").GetString()!=item.Peer||!body.TryGetProperty("message_id",out var mid)||string.IsNullOrWhiteSpace(mid.GetString()))throw new JsonException();return new("sent",ExternalId:mid.GetString());
            }
            if(item.JobId is not null) {
                if(item.Attempts>=240)return new("review",JobId:item.JobId,Code:"zalo_result_pending_too_long");
                if(!Guid.TryParse(item.JobId,out var job))throw new JsonException();var data=await ZaloGet(c,$"api/v1/jobs/{job:D}",ct);
                if(data.GetProperty("jobId").GetGuid()!=job||!data.TryGetProperty("senderProfileId",out var profile)||profile.GetGuid().ToString("D")!=Guid.Parse(c.AccountId).ToString("D"))throw new JsonException();
                var summary=data.GetProperty("summary");if(summary.GetProperty("total").GetInt32()!=1)throw new JsonException();
                if(summary.GetProperty("sent").GetInt32()==1)return new("sent",JobId:item.JobId);
                if(summary.GetProperty("needsReview").GetInt32()>0||summary.GetProperty("partiallySent").GetInt32()>0)return new("review",JobId:item.JobId,Code:"zalo_needs_review");
                if(summary.GetProperty("failed").GetInt32()>0||summary.GetProperty("notFound").GetInt32()>0||summary.GetProperty("skipped").GetInt32()>0)return new("failed",JobId:item.JobId,Code:"zalo_send_failed");
                if(data.GetProperty("status").GetString() is "Cancelled" or "Stopped" or "Failed")return new("failed",JobId:item.JobId,Code:"zalo_job_stopped");
                return new("accepted",JobId:item.JobId);
            }
            if(clock.GetUtcNow()-item.CreatedAt>=TimeSpan.FromHours(23))return new("review",Code:"idempotency_window_expired");
            if(!Guid.TryParse(item.Peer,out var peer)||!long.TryParse(item.Reference,out var sequence)||sequence<=0)throw new JsonException();
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(registry.ZaloOrigin(c),$"api/v1/inbox/conversations/{peer:D}/replies"+(item.Media is null?"":"/media")));
            request.Headers.Authorization=new("Bearer",registry.Secret(c.AccessTokenFile));request.Headers.Add("Idempotency-Key","gao-cskh-"+item.Id.ToString("N"));if(item.Media is null)request.Content=JsonContent.Create(new{replyToSequence=sequence,text=item.Text});
            else{
                CustomerOutgoingMedia.Validate(item.Media);var a=item.Media.Attachment;var form=new MultipartFormDataContent();request.Content=form;
                form.Add(new StringContent(sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)),"ReplyToSequence");
                form.Add(new StringContent(a.Kind+(string.IsNullOrEmpty(item.Text)?"":"ThenText")),"DeliveryMode");
                if(!string.IsNullOrEmpty(item.Text))form.Add(new StringContent(item.Text),"Text");
                var file=new ByteArrayContent(item.Media.Bytes);file.Headers.ContentType=new MediaTypeHeaderValue(a.ContentType);form.Add(file,"Asset",a.FileName);
            }
            var response=await Json(request,ct,Guid.Parse(c.AccountId),media:item.Media is not null);if(response.GetProperty("conversationId").GetGuid()!=peer||response.GetProperty("replyToSequence").GetInt64()!=sequence||response.GetProperty("jobId").GetGuid()==Guid.Empty)throw new JsonException();
            return new("accepted",JobId:response.GetProperty("jobId").GetGuid().ToString("D"));
        }catch(ChannelHttpException e){return new(item.JobId is not null?"accepted":e.Status is >=400 and <500&&e.Status!=408?"failed":c.Provider=="zalomarketing"&&item.Attempts<3?"retry":"review",JobId:item.JobId,Code:"http_"+e.Status);}
        catch(Exception e)when(e is JsonException or InvalidOperationException or KeyNotFoundException){return new("review",JobId:item.JobId,Code:"channel_response_invalid");}
        catch(Exception e)when(e is HttpRequestException or OperationCanceledException){return new(item.JobId is not null?"accepted":c.Provider=="zalomarketing"&&item.Attempts<3&&clock.GetUtcNow()-item.CreatedAt<TimeSpan.FromHours(23)?"retry":"review",JobId:item.JobId,Code:"outcome_unconfirmed");}
    }
    public static bool VerifyFacebook(string secret,ReadOnlySpan<byte> body,string signature) {
        if(signature.Length!=71||!signature.StartsWith("sha256=",StringComparison.Ordinal))return false;
        try{return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature[7..]),HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body));}catch(FormatException){return false;}
    }
    public static CustomerChannelIncoming[] FacebookMessages(CustomerChannelConnection c,JsonElement root,DateTimeOffset now) {
        AssistantJwt.Unique(root);if(root.GetProperty("object").GetString()!="page")throw new JsonException();
        var entries=root.GetProperty("entry");if(entries.GetArrayLength()>20)throw new JsonException();var result=new List<CustomerChannelIncoming>();
        foreach(var entry in entries.EnumerateArray()) {
            if(entry.GetProperty("id").GetString()!=c.AccountId)throw new JsonException();if(!entry.TryGetProperty("messaging",out var messaging))continue;if(messaging.GetArrayLength()>100)throw new JsonException();
            foreach(var message in messaging.EnumerateArray()) {
                if(!message.TryGetProperty("message",out var payload))continue;if(payload.TryGetProperty("is_echo",out var echo)&&echo.ValueKind==JsonValueKind.True)continue;
                if(message.GetProperty("recipient").GetProperty("id").GetString()!=c.AccountId)throw new JsonException();
                var peer=message.GetProperty("sender").GetProperty("id").GetString()!;if(peer==c.AccountId)continue;
                if(peer.Length is <1 or >100||!peer.All(char.IsAsciiDigit))throw new JsonException();
                var mid=payload.GetProperty("mid").GetString()!;var at=DateTimeOffset.FromUnixTimeMilliseconds(message.GetProperty("timestamp").GetInt64());
                if(at<now.AddDays(-7))continue;if(at>now.AddMinutes(5)||mid.Length is <1 or >250)throw new JsonException();
                var isText=payload.TryGetProperty("text",out var text)&&text.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(text.GetString());
                var unsupported=!isText||payload.TryGetProperty("attachments",out _);var content=isText?text.GetString()!:"[Khách gửi nội dung đính kèm. Nhân viên xem tại Facebook để hỗ trợ.]";
                if(content.Length>8000){content=content[..1900]+"\n[Nội dung dài; xem tại Facebook.]";unsupported=true;}
                result.Add(new(mid,peer,content,at,mid,unsupported));if(result.Count>100)throw new JsonException();
            }
        }return result.OrderBy(m=>m.SentAt).ToArray();
    }
    private sealed class ChannelHttpException(int status):Exception {public int Status{get;}=status;}
}