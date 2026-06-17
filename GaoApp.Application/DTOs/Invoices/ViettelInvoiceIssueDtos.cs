namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoiceIssueResultDto
{
    public int InvoiceHeadId { get; set; }

    public bool IsSuccess { get; set; }

    public string? InvoiceNo { get; set; }

    public string? TransactionId { get; set; }

    public string? ReservationCode { get; set; }

    public string? CodeOfTax { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string RawResponse { get; set; } = string.Empty;

    public long DurationMs { get; set; }
}