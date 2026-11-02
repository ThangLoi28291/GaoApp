using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using GaoApp.Application.InvoiceNotifications;

namespace GaoApp.Tests.InvoiceNotifications;
public sealed partial class InvoiceNotificationBundleBuilderTests
{
    private static readonly byte[] SignedXmlFixture="<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Invoice><Data>giả lập &amp; giữ bytes</Data><Signature>TEST-ONLY</Signature></Invoice>\r\n"u8.ToArray();
    private static byte[] OfficialPackage(bool multiple=false)
    {
        using var stream=new MemoryStream();using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            using(var entry=zip.CreateEntry("documents/original.xml").Open())entry.Write(SignedXmlFixture);
            if(multiple){using var second=zip.CreateEntry("metadata.xml").Open();second.Write("<Metadata/>"u8);}
        }
        return stream.ToArray();
    }
    private static InvoiceDocumentSnapshot XmlSnapshot(byte[] package,string kind="OfficialXml")=>Receipt() with
    {
        SourceType="Invoice",SourceId=99,InvoiceId=99,Status="Issued",Kind=kind,IssuedAtUtc=DateTime.UtcNow,
        Artifact=new("uploads/invoices/official.zip","official.zip","application/zip",package.Length,Convert.ToHexString(SHA256.HashData(package)))
    };
    [Fact]public async Task Raw_xml_extraction_preserves_original_entry_bytes_and_parent_package_evidence()
    {
        var (builder,store,files)=Create();var package=OfficialPackage();files.Content=(package,"application/zip","official.zip");var snapshot=XmlSnapshot(package);
        var bundle=await builder.BuildAsync(snapshot,default);var xml=Assert.Single(bundle.Parts,p=>p.Kind=="File");
        Assert.Equal("original.xml",xml.FileName);Assert.Equal("application/xml",xml.ContentType);
        Assert.Equal(SignedXmlFixture,await store.ReadAsync(Item(snapshot,bundle),xml,default));
        Assert.Equal(Convert.ToHexString(SHA256.HashData(package)),snapshot.Artifact!.Sha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(SignedXmlFixture)).ToLowerInvariant(),xml.Sha256);
    }
    [Fact]public async Task Multiple_xml_entries_require_explicit_review_instead_of_guessing_a_document()
    {
        var (builder,_,files)=Create();var package=OfficialPackage(multiple:true);files.Content=(package,"application/zip","official.zip");
        var error=await Assert.ThrowsAsync<InvalidDataException>(()=>builder.BuildAsync(XmlSnapshot(package),default));Assert.Equal("InvoiceXmlSelectionRequired",error.Message);
    }
    [Fact]public async Task Corrupt_xml_crc_is_rejected_even_when_the_package_hash_matches_the_source_record()
    {
        var (builder,_,files)=Create();var package=OfficialPackage();var central=package.AsSpan().IndexOf(new byte[]{80,75,1,2});Assert.True(central>0);
        BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(central+16),0x12345678);files.Content=(package,"application/zip","official.zip");
        await Assert.ThrowsAsync<InvalidDataException>(()=>builder.BuildAsync(XmlSnapshot(package),default));
    }
}
