namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class UploadInputInvoiceXmlRequest
{
    public int StockDocumentId { get; set; }
    public string OriginalFileName { get; set; } = "";
    public byte[] FileBytes { get; set; } = Array.Empty<byte>();
}

public sealed class InputInvoiceXmlUploadResultDto
{
    public int InputInvoiceHeadId { get; set; }
    public int StockDocumentId { get; set; }

    public string? InvoiceTemplateCode { get; set; }
    public string? InvoiceSeries { get; set; }
    public string? InvoiceNumber { get; set; }
    public DateTime? InvoiceDate { get; set; }

    public string? SellerTaxCode { get; set; }
    public string? SellerName { get; set; }

    public decimal TotalBeforeTax { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal TotalPaymentAmount { get; set; }

    public int DetailCount { get; set; }
    public bool IsExistingInvoice { get; set; }
}