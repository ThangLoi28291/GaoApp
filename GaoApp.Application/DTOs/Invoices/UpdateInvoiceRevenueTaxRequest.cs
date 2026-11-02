namespace GaoApp.Application.DTOs.Invoices;

public sealed record UpdateInvoiceRevenueTaxRequest
{
    public int InvoiceHeadId { get; set; }
    public bool Enabled { get; set; }
    public int? Percentage { get; set; }
    public string ExpectedVersion { get; set; } = string.Empty;
}
