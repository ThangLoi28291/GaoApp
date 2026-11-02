using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using GaoApp.Application.InvoiceNotifications;

namespace GaoApp.Infrastructure.Services.InvoiceNotifications;

internal static class InvoiceOriginalXmlExtractor
{
    private static readonly uint[] CrcTable = Enumerable.Range(0,256).Select(i =>
    { var c=(uint)i;for(var j=0;j<8;j++)c=(c&1)!=0?0xEDB88320u^(c>>1):c>>1;return c; }).ToArray();
    public static async Task<(string Name,byte[] Bytes)> ExtractAsync(string zipName,byte[] archiveBytes,CancellationToken ct)
    {
        if(InvoiceNotificationMediaPolicy.Validate("File",zipName,archiveBytes)!="application/zip")throw new InvalidDataException("InvoiceZipInvalid");
        using var input=new MemoryStream(archiveBytes,writable:false);using var archive=new ZipArchive(input,ZipArchiveMode.Read);
        var candidates=archive.Entries.Where(e=>Path.GetExtension(e.FullName).Equals(".xml",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(candidates.Length!=1)throw new InvalidDataException("InvoiceXmlSelectionRequired");
        var entry=candidates[0];if(entry.Length is <1 or >20L*1024*1024)throw new InvalidDataException("InvoiceXmlSizeRejected");
        var name=InvoiceNotificationMediaPolicy.NormalizeFileName(Path.GetFileName(entry.FullName));
        await using var content=entry.Open();await using var output=new MemoryStream();var buffer=new byte[81920];int read;uint crc=0xFFFFFFFF;
        while((read=await content.ReadAsync(buffer,ct))>0)
        {
            if(output.Length+read>20L*1024*1024||output.Length+read>entry.Length)throw new InvalidDataException("InvoiceXmlSizeRejected");
            for(var i=0;i<read;i++)crc=CrcTable[(crc^buffer[i])&255]^(crc>>8);
            await output.WriteAsync(buffer.AsMemory(0,read),ct);
        }
        var bytes=output.ToArray();
        if(bytes.LongLength!=entry.Length||(crc^0xFFFFFFFF)!=CentralCrc(archiveBytes,entry.FullName)||InvoiceNotificationMediaPolicy.Validate("File",name,bytes)!="application/xml")
            throw new InvalidDataException("InvoiceXmlIntegrityRejected");
        // Return the original entry bytes. Never parse/serialize a signed XML document to produce an attachment.
        return(name,bytes);
    }
    private static uint CentralCrc(byte[] bytes,string entryName)
    {
        for(var i=bytes.Length-22;i>=Math.Max(0,bytes.Length-65557);i--)
        {
            if(U32(bytes,i)!=0x06054b50||i+22+U16(bytes,i+20)!=bytes.Length)continue;
            var count=U16(bytes,i+10);var offset=checked((int)U32(bytes,i+16));
            for(var j=0;j<count;j++)
            {
                var length=U16(bytes,offset+28);var name=Encoding.UTF8.GetString(bytes,offset+46,length);
                if(name==entryName)return U32(bytes,offset+16);
                offset+=46+length+U16(bytes,offset+30)+U16(bytes,offset+32);
            }
            break;
        }
        throw new InvalidDataException("InvoiceXmlIntegrityRejected");
    }
    private static ushort U16(byte[] b,int i)=>BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i,2));
    private static uint U32(byte[] b,int i)=>BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i,4));
}
