namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoiceLookupResultDto
{
    public int InvoiceHeadId { get; set; }

    public bool IsFound { get; set; }

    public string TransactionUuid { get; set; } = string.Empty;

    public string? InvoiceNo { get; set; }

    public string? TransactionId { get; set; }

    public string? ReservationCode { get; set; }

    public string? CodeOfTax { get; set; }

    public DateTime? IssueDateUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string RawResponse { get; set; } = string.Empty;

    public long DurationMs { get; set; }
}