namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoicePreviewFileDto
{
    public int InvoiceHeadId { get; set; }

    public string FileName { get; set; } = "viettel-preview.pdf";

    public string ContentType { get; set; } = "application/pdf";

    public byte[] FileBytes { get; set; } = Array.Empty<byte>();

    public string? RawResponsePreview { get; set; }
}