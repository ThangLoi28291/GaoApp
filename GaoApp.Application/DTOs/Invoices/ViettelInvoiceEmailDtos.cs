namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoiceSendEmailRequestDto
{
    public int InvoiceHeadId { get; set; }

    public string BuyerEmail { get; set; } = string.Empty;
}

public class ViettelInvoiceSendEmailResultDto
{
    public int InvoiceHeadId { get; set; }

    public bool IsSuccess { get; set; }

    public string BuyerEmail { get; set; } = string.Empty;

    public string TransactionUuid { get; set; } = string.Empty;

    public string? ProviderInvoiceNo { get; set; }

    public string? Code { get; set; }

    public string? Message { get; set; }

    public string RawResponse { get; set; } = string.Empty;

    public long DurationMs { get; set; }
}