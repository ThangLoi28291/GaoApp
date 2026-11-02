using GaoApp.Domain.Enums;

namespace GaoApp.Application.DTOs.Invoices;

/// <summary>One SQL aggregate per provider status; never carries invoice bodies or errors.</summary>
public sealed class InvoiceDashboardAggregateRowDto
{
    public InvoiceProviderStatus ProviderStatus { get; set; }
    public int Count { get; set; }
    public decimal Amount { get; set; }
    public int IssuedCount { get; set; }
    public decimal IssuedAmount { get; set; }
    public int NeedUuidSyncCount { get; set; }
    public int PdfDownloadedCount { get; set; }
    public int ZipDownloadedCount { get; set; }
    public int MissingPdfCount { get; set; }
    public int MissingZipCount { get; set; }
}
