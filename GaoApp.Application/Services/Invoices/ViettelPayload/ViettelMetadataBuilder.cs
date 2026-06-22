using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Services.Invoices;

internal static class ViettelMetadataBuilder
{
    public static List<ViettelMetadataDto> Build(InvoiceHeadDto invoice)
    {
        var metadata = new List<ViettelMetadataDto>();

        var invoiceNote = ViettelPayloadTextHelper.NormalizeInvoiceNote(invoice.Note);

        if (!string.IsNullOrWhiteSpace(invoiceNote))
        {
            metadata.Add(new ViettelMetadataDto
            {
                KeyTag = "invoiceNote",
                ValueType = "text",
                Value = invoiceNote
            });
        }

        return metadata;
    }
}