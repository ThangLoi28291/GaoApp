using System.Security.Cryptography;
using System.Text;
using GaoApp.Application.Interfaces.Services.Invoices;
using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.InvoiceNotifications;

public sealed partial class InvoiceNotificationBundleBuilderTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"GaoAppP3BuilderTests",Guid.NewGuid().ToString("N"));
    private class EnvironmentStub(string root):IHostEnvironment
    { public string EnvironmentName{get;set;}="Development";public string ApplicationName{get;set;}="P3_TEST";public string ContentRootPath{get;set;}=root;public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider(); }
    private sealed class Files: IInvoiceFileStorage
    {
        public (byte[] Bytes,string ContentType,string FileName)? Content;
        public int Reads;
        public Task<string> SaveAsync(string folder,string name,byte[] bytes,CancellationToken ct=default)=>throw new InvalidOperationException("Provider files must never be overwritten by notification preparation.");
        public Task<(byte[] Bytes,string ContentType,string FileName)?> ReadAsync(string path,CancellationToken ct=default){Reads++;return Task.FromResult(Content);}
    }
    private (InvoiceNotificationBundleBuilder Builder,PrivateInvoicePreparedAssetStore Store,Files Files) Create(bool images=false)
    {
        Directory.CreateDirectory(directory);var options=Options.Create(new InvoiceNotificationArtifactOptions{RootDirectory=Path.Combine(directory,"private"),IncludeReceiptImages=images});
        var store=new PrivateInvoicePreparedAssetStore(options,new EnvironmentStub(directory));var files=new Files();return(new(files,store,options),store,files);
    }
    private static InvoiceDocumentSnapshot Receipt()=>new()
    {
        StoreId=1,CustomerId=7,SourceType="Order",SourceId=11,OrderId=11,SourceVersion="SYNTHETIC-V1",Kind="Receipt",Code="TEST-BILL-11",
        Status="Completed",StoreName="Cửa hàng thử nghiệm",CustomerName="Khách giả lập",CustomerPhone="0398946174",FinalizedAtUtc=new DateTime(2026,10,27,2,30,0,DateTimeKind.Utc),
        Lines=[new(1,"Gạo thử", "kg",2m,10000m,1000m,18000m){PromotionDiscount=1000m},new(2,"Quà tặng thử","gói",1m,0m,0m,0m){IsPromotionGift=true}],
        Subtotal=20000m,LineDiscountTotal=1000m,PromotionDiscountTotal=1000m,ComboDiscountTotal=2000m,OrderDiscount=1000m,VoucherDiscountTotal=1000m,DiscountTotal=6000m,GrandTotal=14000m,
        DepositAmount=1000m,Payments=[new("Cash",10000m,"TEST-CASH"),new("Bank",5000m,"TEST-TRANSFER")],PaidTotal=16000m,ChangeDue=2000m,BalanceDue=0m,CurrentPoints=1.125m
    };
    private static InvoiceNotificationWorkItem Item(InvoiceDocumentSnapshot s,InvoicePreparedBundle p)=>new(Guid.NewGuid(),Guid.NewGuid(),1,7,s.SourceType,s.SourceId,s.SourceVersion,s.Kind,Guid.NewGuid(),"TEST-IDEMPOTENCY","Dispatching",DateTime.UtcNow,
        new(Guid.NewGuid(),1,7,"zalo-test","test-binding",Guid.NewGuid(),Guid.NewGuid(),42,"sdk-v1:123456","0398946174",1,DateTime.UtcNow,null),s,p,null,1);
    private static string Text(InvoicePreparedBundle b)=>string.Concat(b.Parts.Where(p=>p.Kind=="Text").Select(p=>p.Text));

    [Fact]public async Task Receipt_preserves_discounts_payments_deposit_fractional_points_and_every_line()
    {
        var (builder,_,files)=Create();var snapshot=Receipt();var bundle=await builder.BuildAsync(snapshot,default);var text=Text(bundle);
        Assert.Contains("Gạo thử",text);Assert.Contains("Quà tặng thử",text);Assert.Contains("hàng tặng",text);Assert.Contains("1,125",text);
        Assert.Contains("TEST-CASH",text);Assert.Contains("TEST-TRANSFER",text);Assert.Contains("đặt cọc",text);Assert.Contains("UTC+7",text);
        Assert.Contains("14000",text);Assert.Contains("6000",text);Assert.Equal(0,files.Reads);Assert.All(bundle.Parts,p=>Assert.InRange(p.Text!.EnumerateRunes().Count(),1,4000));
        Assert.Equal(bundle.Fingerprint,(await builder.BuildAsync(snapshot,default)).Fingerprint);
    }
    [Fact]public async Task Unknown_points_stay_unknown_and_credit_balances_are_not_fabricated_payments()
    {
        var (builder,_,_)=Create();var s=Receipt() with{CurrentPoints=null,DepositAmount=0,Payments=[],PaidTotal=0,ChangeDue=0,BalanceDue=14000,PaymentStatus="Unpaid"};
        var text=Text(await builder.BuildAsync(s,default));Assert.Contains("chưa xác định",text);Assert.Contains("Còn phải thu: 14000",text);Assert.DoesNotContain("Thanh toán tiền mặt",text);
    }
    [Theory][InlineData("total")][InlineData("line")][InlineData("payment")][InlineData("draft")]
    public async Task Inconsistent_or_unfinalized_snapshot_never_creates_sendable_bundle(string change)
    {
        var (builder,_,_)=Create();var s=Receipt();s=change switch
        {"total"=>s with{GrandTotal=1},"line"=>s with{Lines=[s.Lines[0] with{LineTotal=19000},s.Lines[1]]},"payment"=>s with{PaidTotal=9999},_=>s with{Status="Draft"}};
        await Assert.ThrowsAsync<InvalidDataException>(()=>builder.BuildAsync(s,default));
    }
    [Fact]public async Task More_than_fifty_lines_and_unicode_are_preserved_without_truncation()
    {
        var (builder,_,_)=Create();var lines=Enumerable.Range(1,75).Select(i=>new InvoiceDocumentLine(i,$"Hàng thử {i} 😀 "+new string('x',70),"kg",1,10,0,10)).ToArray();
        var s=Receipt() with{Lines=lines,Subtotal=750,LineDiscountTotal=0,PromotionDiscountTotal=0,ComboDiscountTotal=0,OrderDiscount=0,VoucherDiscountTotal=0,DiscountTotal=0,GrandTotal=750,DepositAmount=0,Payments=[new("Cash",750,null)],PaidTotal=750,ChangeDue=0};
        var result=await builder.BuildAsync(s,default);Assert.True(result.Parts.Count>1);Assert.Contains("Hàng thử 75",Text(result));Assert.Contains("😀",Text(result));
        Assert.All(result.Parts,p=>Assert.InRange(p.Text!.EnumerateRunes().Count(),1,4000));
    }
    [Fact]public async Task Receipt_image_is_valid_private_png_and_original_text_remains_available()
    {
        var (builder,store,_)=Create(images:OperatingSystem.IsWindows());var s=Receipt();var bundle=await builder.BuildAsync(s,default);
        if(!OperatingSystem.IsWindows()){Assert.All(bundle.Parts,p=>Assert.Equal("Text",p.Kind));return;}
        var image=Assert.Single(bundle.Parts,p=>p.Kind=="Image");var bytes=await store.ReadAsync(Item(s,bundle),image,default);
        Assert.True(bytes.AsSpan().StartsWith(new byte[]{137,80,78,71,13,10,26,10}));Assert.Contains("Gạo thử",Text(bundle));
        var qa=Environment.GetEnvironmentVariable("P3_QA_OUTPUT_DIR");if(!string.IsNullOrWhiteSpace(qa)){Directory.CreateDirectory(qa);await File.WriteAllBytesAsync(Path.Combine(qa,"receipt-synthetic.png"),bytes);}
    }
    [Theory][InlineData(".pdf","application/pdf","OfficialPdf")][InlineData(".xml","application/xml","OfficialZipXml")][InlineData(".zip","application/zip","OfficialZipXml")]
    public async Task Official_artifacts_are_byte_for_byte_unchanged_and_hash_bound(string extension,string contentType,string kind)
    {
        var (builder,store,files)=Create();var bytes=extension==".pdf"?"%PDF-1.4 SYNTHETIC"u8.ToArray():extension==".xml"?"<Invoice>synthetic &amp; test</Invoice>"u8.ToArray():Zip();
        files.Content=(bytes,contentType,"test"+extension);var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var s=Receipt() with{SourceType="Invoice",SourceId=99,InvoiceId=99,Status="Issued",Kind=kind,IssuedAtUtc=DateTime.UtcNow,Artifact=new("uploads/invoices/test"+extension,"test"+extension,contentType,bytes.Length,hash)};
        var bundle=await builder.BuildAsync(s,default);var part=Assert.Single(bundle.Parts,p=>p.Kind=="File");
        Assert.Equal(bytes,await store.ReadAsync(Item(s,bundle),part,default));Assert.Equal(hash.ToLowerInvariant(),part.Sha256);Assert.DoesNotContain("uploads/",Text(bundle));
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadAsync(Item(s,bundle) with{StoreId=2},part,default));
    }
    [Fact]public async Task Changed_provider_file_and_unissued_invoice_are_blocked()
    {
        var (builder,_,files)=Create();var bytes="%PDF-SYNTHETIC"u8.ToArray();files.Content=(bytes,"application/pdf","test.pdf");
        var s=Receipt() with{SourceType="Invoice",SourceId=99,InvoiceId=99,Status="Issued",Kind="OfficialPdf",IssuedAtUtc=DateTime.UtcNow,Artifact=new("uploads/invoices/test.pdf","test.pdf","application/pdf",bytes.Length,new string('A',64))};
        await Assert.ThrowsAsync<InvalidDataException>(()=>builder.BuildAsync(s,default));
        var reads=files.Reads;await Assert.ThrowsAsync<InvalidDataException>(()=>builder.BuildAsync(s with{Status="Cancelled"},default));Assert.Equal(reads,files.Reads);
    }
    [Fact]public async Task Prepared_file_tampering_cannot_change_a_retry_payload()
    {
        var (builder,store,files)=Create();var bytes="%PDF-SYNTHETIC"u8.ToArray();files.Content=(bytes,"application/pdf","test.pdf");
        var s=Receipt() with{SourceType="Invoice",SourceId=99,InvoiceId=99,Status="Issued",Kind="OfficialPdf",IssuedAtUtc=DateTime.UtcNow,Artifact=new("uploads/invoices/test.pdf","test.pdf","application/pdf",bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)))};
        var bundle=await builder.BuildAsync(s,default);var part=Assert.Single(bundle.Parts,p=>p.Kind=="File");var file=Path.Combine(directory,"private",part.StorageKey!);
        File.SetAttributes(file,FileAttributes.Normal);await File.WriteAllBytesAsync(file,"%PDF-MODIFIED"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(()=>store.ReadAsync(Item(s,bundle),part,default));
    }
    private static byte[] Zip(){using var bytes=new MemoryStream();using(var zip=new System.IO.Compression.ZipArchive(bytes,System.IO.Compression.ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry("test.xml").Open());writer.Write("<Invoice>synthetic</Invoice>");}return bytes.ToArray();}
    public void Dispose()
    {
        var expected=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"GaoAppP3BuilderTests"))+Path.DirectorySeparatorChar;
        if(!Path.GetFullPath(directory).StartsWith(expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unsafe cleanup.");
        if(!Directory.Exists(directory))return;foreach(var f in Directory.EnumerateFiles(directory,"*",SearchOption.AllDirectories))File.SetAttributes(f,FileAttributes.Normal);Directory.Delete(directory,true);
    }
}
