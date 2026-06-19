namespace GaoApp.Application.DTOs.Invoices;

public class ViettelInvoiceListSyncRequestDto
{
    public DateTime FromDate { get; set; } = DateTime.Today;

    public DateTime ToDate { get; set; } = DateTime.Today;

    public int PageSize { get; set; } = 100;

    public bool UpdateLocalInvoices { get; set; } = true;
}

public class ViettelInvoiceListItemDto
{
    public string? InvoiceId { get; set; }

    public string? InvoiceType { get; set; }

    public string? TemplateCode { get; set; }

    public string? InvoiceSeri { get; set; }

    public string? InvoiceNumber { get; set; }

    public string? InvoiceNo { get; set; }

    public string? Currency { get; set; }

    public decimal Total { get; set; }

    public decimal TotalBeforeTax { get; set; }

    public decimal TaxAmount { get; set; }

    public long? IssueDate { get; set; }

    public string? IssueDateStr { get; set; }

    public int? State { get; set; }

    public int? StateCode { get; set; }

    public int? PaymentStatus { get; set; }

    public string? PaymentStatusName { get; set; }

    public string? BuyerName { get; set; }

    public string? BuyerTaxCode { get; set; }

    public string? SupplierTaxCode { get; set; }

    public string? TransactionUuid { get; set; }

    public string? OriginalInvoiceId { get; set; }
}

public class ViettelInvoiceListSyncResultDto
{
    public DateTime FromDate { get; set; }

    public DateTime ToDate { get; set; }

    public int TotalRemoteRows { get; set; }

    public int RemoteItemsLoaded { get; set; }

    public int MatchedByTransactionUuid { get; set; }

    public int MatchedByInvoiceNo { get; set; }

    public int UpdatedLocalInvoices { get; set; }

    public int NotMatched { get; set; }

    public int ErrorCount { get; set; }

    public long DurationMs { get; set; }

    public List<ViettelInvoiceListItemDto> RemoteInvoices { get; set; } = new();

    public List<string> Messages { get; set; } = new();
}