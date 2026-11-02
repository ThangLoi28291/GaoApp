using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace GaoApp.Tests.InvoiceNotifications;
public sealed partial class InvoiceNotificationBundleBuilderTests
{
    private sealed class Links:IInvoiceOfficialDocumentLinkFactory
    {
        public int Calls;
        public Task<string> CreateAsync(InvoiceNotificationWorkItem item,InvoiceOfficialDocumentLinkMetadata pdf,CancellationToken ct)
        {Calls++;Assert.Equal(item.Snapshot.Artifact!.Sha256,pdf.Sha256);return Task.FromResult("https://shop.test/invoice-document?token=TEST-PROTECTED-LINK");}
    }
    [Fact]public async Task Link_notification_validates_original_pdf_but_sends_only_text_and_the_scoped_URL()
    {
        var (_,store,files)=Create();var bytes="%PDF-SYNTHETIC"u8.ToArray();files.Content=(bytes,"application/pdf","test.pdf");
        var snapshot=Receipt() with{SourceType="Invoice",SourceId=99,InvoiceId=99,Status="Issued",Kind="OfficialLink",IssuedAtUtc=DateTime.UtcNow,Artifact=new("uploads/invoices/test.pdf","test.pdf","application/pdf",bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)))};
        var links=new Links();var builder=new InvoiceNotificationBundleBuilder(files,store,Options.Create(new InvoiceNotificationArtifactOptions{IncludeReceiptImages=false}),links);
        var work=Item(snapshot,new([],"unused")) with{Prepared=null};var bundle=await builder.BuildAsync(work,default);
        Assert.Equal(1,links.Calls);Assert.All(bundle.Parts,p=>Assert.Equal("Text",p.Kind));Assert.Contains("https://shop.test/invoice-document?token=",Text(bundle));
        Assert.DoesNotContain("Tệp đính kèm",Text(bundle));Assert.DoesNotContain("uploads/",Text(bundle));Assert.Equal(1,files.Reads);
    }
}
