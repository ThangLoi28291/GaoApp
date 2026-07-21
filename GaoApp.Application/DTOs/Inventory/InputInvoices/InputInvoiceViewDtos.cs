namespace GaoApp.Application.DTOs.Inventory.InputInvoices;

public sealed class InputInvoiceHeadDto
{
    public int Id { get; set; }

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

    public List<InputInvoiceDetailDto> Details { get; set; } = new();
}

public sealed class InputInvoiceDetailDto
{
    public int Id { get; set; }
    public int LineNo { get; set; }

    public string ItemName { get; set; } = "";
    public string? UnitName { get; set; }

    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }

    public string? VatRate { get; set; }
    public decimal VatAmount { get; set; }
}