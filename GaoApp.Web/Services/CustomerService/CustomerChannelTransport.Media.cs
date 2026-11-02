using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GaoApp.Web.Services.Assistant;

namespace GaoApp.Web.Services.CustomerService;

public sealed partial class CustomerChannelTransport
{
    public async Task<CustomerZaloMediaContent> ReadMedia(CustomerChannelConnection connection,CustomerZaloMediaTarget target,bool download,CancellationToken ct) {
        if(connection.Provider!="zalomarketing"||!connection.Enabled||connection.Binding!=target.Binding||
            !Guid.TryParse(target.Peer,out var peer)||target.Sequence<=0||target.Attachment.Id==Guid.Empty)
            throw new AssistantException(404,"customer_media_not_found","Không tìm thấy tệp của hội thoại.");
        if(target.Attachment.Length>CustomerZaloMedia.MaximumBytes)
            throw new AssistantException(413,"customer_media_too_large","Tệp vượt 30 MB. Vui lòng xem tại ZaloMarketing.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        using var request=new HttpRequestMessage(HttpMethod.Get,new Uri(registry.ZaloOrigin(connection),
            $"api/v1/inbox/conversations/{peer:D}/messages/{target.Sequence}/media/{target.Attachment.Id:D}"));
        request.Headers.Authorization=new("Bearer",registry.Secret(connection.AccessTokenFile));
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.StatusCode==HttpStatusCode.NotFound)throw new AssistantException(404,"customer_media_unavailable","Tệp chưa được đồng bộ hoặc đã hết thời gian lưu.");
        if(!response.IsSuccessStatusCode)throw new AssistantException(502,"customer_media_source_unavailable","Chưa tải được tệp từ ZaloMarketing.");
        if(!response.Headers.TryGetValues("X-ZaloMarketing-Sender-Profile-Id",out var identities)||identities.Count()!=1||
            !Guid.TryParseExact(identities.Single(),"D",out var profile)||profile!=Guid.Parse(connection.AccountId))
            throw new AssistantException(502,"customer_media_profile_mismatch","Tài khoản nguồn của tệp không khớp.");
        var actualType=response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
        if(actualType!=target.Attachment.ContentType||response.Content.Headers.ContentLength is long size&&size!=target.Attachment.Length)
            throw new AssistantException(502,"customer_media_contract_mismatch","Thông tin tệp nguồn đã thay đổi.");
        await using var source=await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output=new MemoryStream();var buffer=new byte[65536];
        while(true){var count=await source.ReadAsync(buffer,deadline.Token);if(count==0)break;
            if(output.Length+count>target.Attachment.Length||output.Length+count>CustomerZaloMedia.MaximumBytes)
                throw new AssistantException(502,"customer_media_size_mismatch","Kích thước tệp nguồn không khớp.");
            await output.WriteAsync(buffer.AsMemory(0,count),deadline.Token);
        }
        if(output.Length!=target.Attachment.Length)throw new AssistantException(502,"customer_media_size_mismatch","Tệp nguồn chưa được tải đầy đủ.");
        var bytes=output.ToArray();
        var preview=!download&&target.Attachment.Kind is "Image" or "Sticker"&&CustomerZaloMedia.ImageType(actualType!);
        if(preview&&!CustomerZaloMedia.ImageSignature(bytes,actualType!))throw new AssistantException(502,"customer_media_content_invalid","Nội dung ảnh nguồn không hợp lệ.");
        return new(bytes,preview?actualType!:"application/octet-stream",CustomerZaloMedia.FileName(target.Attachment.FileName),preview);
    }
}
