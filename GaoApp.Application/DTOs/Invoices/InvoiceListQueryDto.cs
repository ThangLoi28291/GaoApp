namespace GaoApp.Application.DTOs.Invoices;

public class InvoiceListQueryDto
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }

    public int? OrderId { get; set; }

    public string? Keyword { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}