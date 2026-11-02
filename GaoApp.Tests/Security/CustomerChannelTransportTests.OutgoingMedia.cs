using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Security.Cryptography;
using GaoApp.Web.Services.CustomerService;
using Xunit;
namespace GaoApp.Tests.Security;
public sealed partial class CustomerChannelTransportTests
{
    [Theory][InlineData("Image","")][InlineData("File","TEST caption")]
    public async Task Zalo_media_retries_use_frozen_bytes_key_target_and_then_poll_without_upload(string kind,string text){
        var c=Zalo();var peer=Guid.NewGuid();var job=Guid.NewGuid();var bytes=kind=="Image"?new byte[]{137,80,78,71,13,10,26,10,1}:"%PDF-1.7 TEST ONLY"u8.ToArray();
        var asset=new CustomerOutgoingAsset(new(kind,kind=="Image"?"TEST.png":"TEST.pdf",kind=="Image"?"image/png":"application/pdf",bytes.Length),Convert.ToHexString(SHA256.HashData(bytes)),bytes);
        var item=Item() with{Peer=peer.ToString(),Text=text,Media=asset};var keys=new List<string>();var captured=new List<byte[]>();var calls=0;
        using var http=new HttpClient(new Handler(async r=>{
            calls++;if(r.Method==HttpMethod.Get){Assert.EndsWith("/api/v1/jobs/"+job,r.RequestUri!.ToString());return Json(new{jobId=job,senderProfileId=Guid.Parse(c.AccountId),status="Completed",summary=new{total=1,sent=1,needsReview=0,partiallySent=0,failed=0,notFound=0,skipped=0}});}
            Assert.EndsWith($"/api/v1/inbox/conversations/{peer}/replies/media",r.RequestUri!.ToString());Assert.Equal("multipart/form-data",r.Content!.Headers.ContentType!.MediaType);keys.Add(r.Headers.GetValues("Idempotency-Key").Single());
            var form=Assert.IsType<MultipartFormDataContent>(r.Content);var fields=form.ToDictionary(f=>f.Headers.ContentDisposition!.Name!.Trim('"'));
            Assert.Equal("42",await fields["ReplyToSequence"].ReadAsStringAsync());Assert.Equal(kind+(text==""?"":"ThenText"),await fields["DeliveryMode"].ReadAsStringAsync());
            if(text=="")Assert.False(fields.ContainsKey("Text"));else Assert.Equal(text,await fields["Text"].ReadAsStringAsync());captured.Add(await fields["Asset"].ReadAsByteArrayAsync());
            if(calls==1)throw new HttpRequestException("TEST lost acceptance response");var result=Json(new{jobId=job,conversationId=peer,replyToSequence=42});result.Headers.Add("X-ZaloMarketing-Sender-Profile-Id",c.AccountId);return result;
        }));
        var wire=new CustomerChannelTransport(http,Registry(c),Clock);Assert.Equal("retry",(await wire.Dispatch(c,item,default)).State);var result=await wire.Dispatch(c,item with{Attempts=1},default);Assert.Equal("accepted",result.State);Assert.Equal(keys[0],keys[1]);Assert.All(captured,b=>Assert.Equal(bytes,b));
        Assert.Equal("sent",(await wire.Dispatch(c,item with{JobId=job.ToString(),Attempts=2},default)).State);Assert.Equal(3,calls);Assert.Equal(2,captured.Count);
    }
}
