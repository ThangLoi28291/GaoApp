using System.Net;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using GaoApp.Infrastructure.Printing;
using GaoApp.LabelPrintClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GaoApp.Tests.Printing;

[SupportedOSPlatform("windows")]
public sealed class LabelPrintClientTests
{
    const string Origin = "https://gao.test", Token = "test-pairing-token-at-least-32-characters";
    private static ClientPrintRequest Request() => new(Guid.NewGuid(), new(ProductLabelLayouts.DefaultDesign("promotion-shelf"),
        new("Client tem", "Label USB", 203, 108, 0, 0), [new(ProductLabelRenderer.SampleFor(ProductLabelLayouts.DefaultDesign("promotion-shelf")), 3)]));

    [Fact]
    public async Task Loopback_http_requires_origin_and_pairing_and_prints_directly_without_sql_or_dialog()
    {
        await using var fixture=await Start();
        using var client=new HttpClient {BaseAddress=fixture.Http.BaseAddress};
        Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync("/health")).StatusCode);
        client.DefaultRequestHeaders.Add("Origin",Origin);
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/health")).StatusCode);
        client.DefaultRequestHeaders.Add("Authorization","Bearer "+Token);
        var health=await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/health");
        Assert.Equal("gao-label-client-v1",health.GetProperty("protocol").GetString());
        using var preflight=new HttpRequestMessage(HttpMethod.Options,"/print");
        preflight.Headers.Add("Origin",Origin);preflight.Headers.Add("Access-Control-Request-Method","POST");preflight.Headers.Add("Access-Control-Request-Headers","authorization,content-type");
        var preflightResponse=await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent,preflightResponse.StatusCode);
        Assert.Equal(Origin,Assert.Single(preflightResponse.Headers.GetValues("Access-Control-Allow-Origin")));
        client.DefaultRequestHeaders.Remove("Origin");client.DefaultRequestHeaders.Add("Origin","https://other.test");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync("/print",Request())).StatusCode);Assert.Equal(0,fixture.Transport.Calls);
        var request=Request();
        var response=await fixture.Http.PostAsJsonAsync("/print",request);response.EnsureSuccessStatusCode();
        Assert.Equal(1,fixture.Transport.Calls);Assert.Equal("Label USB",fixture.Transport.Printer);
        Assert.StartsWith("SIZE 52 mm,30 mm",System.Text.Encoding.ASCII.GetString(fixture.Transport.Commands[0]));
        var result=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(3,result.GetProperty("quantity").GetInt32());Assert.True(result.GetProperty("submitted").GetBoolean());
        var duplicate=await fixture.Http.PostAsJsonAsync("/print",request);duplicate.EnsureSuccessStatusCode();Assert.Equal(1,fixture.Transport.Calls);
        var otherPayload=request with {Payload=request.Payload with {Items=[new(request.Payload.Items[0].Product,1)]}};
        Assert.Equal(HttpStatusCode.Conflict,(await fixture.Http.PostAsJsonAsync("/print",otherPayload)).StatusCode);
        var status=await fixture.Http.GetFromJsonAsync<System.Text.Json.JsonElement>("/results/"+request.RequestId);Assert.True(status.GetProperty("submitted").GetBoolean());
        var unknown=Request();unknown=unknown with {Payload=unknown.Payload with {Printer=unknown.Payload.Printer with {WindowsPrinterName="Not installed"}}};
        Assert.Equal(HttpStatusCode.BadRequest,(await fixture.Http.PostAsJsonAsync("/print",unknown)).StatusCode);Assert.Equal(1,fixture.Transport.Calls);
    }

    [Fact]
    public async Task Uncertain_spool_write_is_not_replayed_after_process_restart()
    {
        string state=State();var request=Request();
        await using(var first=await Start(state,true))
        {Assert.Equal(HttpStatusCode.InternalServerError,(await first.Http.PostAsJsonAsync("/print",request)).StatusCode);Assert.Equal(1,first.Transport.Calls);}
        await using(var restarted=await Start(state))
        {Assert.Equal(HttpStatusCode.Conflict,(await restarted.Http.PostAsJsonAsync("/print",request)).StatusCode);Assert.Equal(0,restarted.Transport.Calls);}
    }

    [Theory]
    [InlineData(203,1,50,30)] [InlineData(300,2,50,30)] [InlineData(203,1,80,50)] [InlineData(300,1,80,50)]
    public async Task Preview_and_print_share_geometry_offsets_and_discount_prices(int dpi,int columns,int width,int height)
    {
        await using var fixture=await Start();var request=Request();
        var design=request.Payload.Design with {Columns=columns,WidthMm=width,HeightMm=height};var printer=request.Payload.Printer with {Dpi=dpi,OffsetXmm=0.5m,OffsetYmm=0.5m};
        var product=request.Payload.Items[0].Product;
        var response=await fixture.Http.PostAsJsonAsync("/preview",new ClientPreviewRequest(design,product,printer));response.EnsureSuccessStatusCode();
        Assert.Equal("image/png",response.Content.Headers.ContentType!.MediaType);
        var actual=await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(ProductLabelRenderer.Preview(design,product,dpi,0.5m,0.5m),actual);
        var target=Path.Combine("TestResults","promotion-labels","native");Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(Path.Combine(target,width==50?$"preview-{dpi}-{columns}.png":$"preview-{width}x{height}-{dpi}.png"),actual);
        var invalid=printer with {PrintableWidthMm=50};
        Assert.Equal(HttpStatusCode.BadRequest,(await fixture.Http.PostAsJsonAsync("/preview",new ClientPreviewRequest(design,product,invalid))).StatusCode);
    }
    [Theory]
    [InlineData(4, 1)] [InlineData(5, 1)] [InlineData(10, 1)] [InlineData(11, 2)]
    public async Task A4_preview_and_print_use_client_driver_once_with_exact_quantity_and_sheet_count(int quantity,int pages)
    {
        await using var fixture=await Start();var original=Request();
        var request=original with {Payload=original.Payload with {Design=PromotionA4Layout.DefaultDesign(),
            Printer=original.Payload.Printer with {Dpi=300,PrintableWidthMm=210},Items=[new(original.Payload.Items[0].Product,quantity)]}};
        var health=await fixture.Http.GetFromJsonAsync<System.Text.Json.JsonElement>("/health");
        Assert.Contains("promotion-a4-v1",health.GetProperty("features").EnumerateArray().Select(x=>x.GetString()));
        using var preview=await fixture.Http.PostAsJsonAsync("/preview-a4",new PromotionA4Preview(request.Payload,pages));
        preview.EnsureSuccessStatusCode();
        var expectedPreview=PromotionA4Layout.Preview(request.Payload,pages);
        var actualPreview=await preview.Content.ReadAsByteArrayAsync();
        var previewEvidence=Path.GetFullPath(Path.Combine("TestResults","promotion-a4"));Directory.CreateDirectory(previewEvidence);
        await File.WriteAllBytesAsync(Path.Combine(previewEvidence,$"http-{quantity}-expected.png"),expectedPreview);
        await File.WriteAllBytesAsync(Path.Combine(previewEvidence,$"http-{quantity}-actual.png"),actualPreview);
        Assert.Equal(PixelHash(expectedPreview),PixelHash(actualPreview));
        var response=await fixture.Http.PostAsJsonAsync("/print",request);response.EnsureSuccessStatusCode();
        var result=await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(quantity,result.GetProperty("quantity").GetInt32());Assert.Equal(pages,result.GetProperty("pages").GetInt32());
        Assert.Equal(System.Text.Json.JsonValueKind.Null,result.GetProperty("spoolJobId").ValueKind);
        Assert.Equal(1,fixture.A4.Calls);Assert.Equal(0,fixture.Transport.Calls);
        var duplicate=await fixture.Http.PostAsJsonAsync("/print",request);duplicate.EnsureSuccessStatusCode();
        Assert.Equal(1,fixture.A4.Calls);
        var status=await fixture.Http.GetFromJsonAsync<System.Text.Json.JsonElement>("/results/"+request.RequestId);
        Assert.True(status.GetProperty("submitted").GetBoolean());
    }
    [Fact]
    public async Task Invalid_A4_driver_is_rejected_before_claim_and_uncertain_driver_send_is_not_replayed()
    {
        var original=Request();var request=original with {Payload=original.Payload with {Design=PromotionA4Layout.DefaultDesign(),
            Printer=original.Payload.Printer with {Dpi=300,PrintableWidthMm=210}}};
        string state=State();
        await using(var fixture=await Start(state))
        {
            fixture.A4.Reject=true;
            Assert.Equal(HttpStatusCode.BadRequest,(await fixture.Http.PostAsJsonAsync("/print",request)).StatusCode);
            Assert.False(File.Exists(Path.Combine(state,request.RequestId.ToString("N")+".json")));
            fixture.A4.Reject=false;fixture.A4.Fail=true;
            Assert.Equal(HttpStatusCode.InternalServerError,(await fixture.Http.PostAsJsonAsync("/print",request)).StatusCode);
            Assert.Equal(1,fixture.A4.Calls);Assert.Equal(0,fixture.Transport.Calls);
        }
        await using(var restarted=await Start(state))
        {
            Assert.Equal(HttpStatusCode.Conflict,(await restarted.Http.PostAsJsonAsync("/print",request)).StatusCode);
            Assert.Equal(0,restarted.A4.Calls);Assert.Equal(0,restarted.Transport.Calls);
        }
    }
    private static string State() => Path.GetFullPath(Path.Combine("TestResults","promotion-labels","client-journal",Guid.NewGuid().ToString("N")));
    private static async Task<Fixture> Start(string? state=null,bool fail=false)
    {
        var transport=new FakeTransport{Fail=fail};var builder=WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName="Testing"});builder.Logging.ClearProviders();
        var a4=new FakeA4Transport();
        var app=LabelClientHost.Build(builder,new(){Port=0,AllowedOrigins=[Origin],PairingToken=Token,StateDirectory=state??State()},transport,()=>["Label USB"],a4);
        await app.StartAsync();
        var http=new HttpClient{BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
        http.DefaultRequestHeaders.Add("Origin",Origin);http.DefaultRequestHeaders.Add("Authorization","Bearer "+Token);
        return new(app,http,transport,a4);
    }
    sealed record Fixture(WebApplication App,HttpClient Http,FakeTransport Transport,FakeA4Transport A4):IAsyncDisposable
    {public async ValueTask DisposeAsync(){Http.Dispose();await App.StopAsync();await App.DisposeAsync();}}
    sealed class FakeTransport:ILabelPrintTransport
    {public int Calls;public bool Fail;public string? Printer;public byte[][] Commands=[];
        public int Send(string printerName,string documentName,IEnumerable<byte[]> commands){Calls++;Printer=printerName;Commands=commands.ToArray();if(Fail)throw new IOException("Spool interrupted");return 42;}}
    sealed class FakeA4Transport:IA4LabelPrintTransport
    {
        public int Calls;public bool Fail,Reject;
        public void Validate(LabelPrinterSnapshot printer){if(Reject)throw new GaoApp.Application.Common.Exceptions.ValidationAppException("Driver A4 missing");}
        public int? Send(string documentName,LabelPrintPayload payload){Calls++;if(Fail)throw new IOException("Driver interrupted");return null;}
    }
    private static string PixelHash(byte[] png)
    {
        using var stream=new MemoryStream(png);using var image=new System.Drawing.Bitmap(stream);
        var data=image.LockBits(new System.Drawing.Rectangle(0,0,image.Width,image.Height),System.Drawing.Imaging.ImageLockMode.ReadOnly,System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        try
        {
            var pixels=new byte[Math.Abs(data.Stride)*image.Height];System.Runtime.InteropServices.Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
            return $"{image.Width}x{image.Height}:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
        }
        finally{image.UnlockBits(data);}
    }
}
