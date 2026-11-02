using GaoApp.Application.InvoiceNotifications;
using GaoApp.Infrastructure.Services.InvoiceNotifications;
using GaoApp.Infrastructure.Services.Invoices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

namespace GaoApp.Tests.InvoiceNotifications;
public sealed partial class InvoiceNotificationBundleBuilderTests
{
    private sealed class WebEnvironmentStub(string root,string publicRoot):EnvironmentStub(root),IWebHostEnvironment
    {public string WebRootPath{get;set;}=publicRoot;public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();}
    [Theory][InlineData(false)][InlineData(true)]
    public void Private_store_cannot_overlap_custom_public_upload_or_web_roots(bool customWeb)
    {
        var publicRoot=Path.Combine(directory,"public");
        var options=Options.Create(new InvoiceNotificationArtifactOptions{RootDirectory=Path.Combine(publicRoot,"invoice-blobs")});
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:UploadRoot"]=customWeb?Path.Combine(directory,"uploads"):publicRoot}).Build();
        EnvironmentStub environment=customWeb?new WebEnvironmentStub(directory,publicRoot):new EnvironmentStub(directory);
        Assert.Throws<InvalidOperationException>(()=>new PrivateInvoicePreparedAssetStore(options,environment,config));
    }
    [Fact]public async Task Source_file_reads_check_size_before_loading_provider_bytes()
    {
        var uploads=Path.Combine(directory,"uploads");Directory.CreateDirectory(Path.Combine(uploads,"invoices"));
        var bytes="%PDF-1.4 SYNTHETIC PROVIDER DOCUMENT"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(uploads,"invoices","test.pdf"),bytes);
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Storage:UploadRoot"]=uploads}).Build();
        var storage=new InvoiceFileStorage(new EnvironmentStub(directory),config);
        await Assert.ThrowsAsync<InvalidDataException>(()=>storage.ReadBoundedAsync("uploads/invoices/test.pdf",8,default));
        var read=await storage.ReadBoundedAsync("uploads/invoices/test.pdf",1024,default);Assert.NotNull(read);Assert.Equal(bytes,read.Value.Bytes);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>storage.ReadBoundedAsync("../outside.pdf",1024,default));
    }
}
