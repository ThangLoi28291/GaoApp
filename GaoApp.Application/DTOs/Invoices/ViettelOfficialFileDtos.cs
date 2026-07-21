namespace GaoApp.Application.DTOs.Invoices;

public enum ViettelOfficialFileType
{
    Pdf = 1,
    ZipXml = 2
}

public class ViettelOfficialFileResultDto
{
    public int InvoiceHeadId { get; set; }

    public ViettelOfficialFileType FileType { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public byte[] FileBytes { get; set; } = Array.Empty<byte>();

    public string? StoredPath { get; set; }

    public string? RawResponsePreview { get; set; }
}