namespace GaoApp.Application.DTOs.Invoices;

public class UnlockInvoiceRequest
{
    public int InvoiceHeadId { get; set; }

    public int? UserId { get; set; }

    public string? Reason { get; set; }
}