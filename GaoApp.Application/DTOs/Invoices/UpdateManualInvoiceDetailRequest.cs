namespace GaoApp.Application.DTOs.Invoices;

public class UpdateManualInvoiceDetailRequest
{
    public int InvoiceDetailId { get; set; }

    public decimal Quantity { get; set; }

    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }

    public string? Note { get; set; }
}