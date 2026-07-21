using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceListClient
{
    Task<Result<ViettelInvoiceListSyncResultDto>> GetInvoicesAsync(
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        DateTime fromDate,
        DateTime toDate,
        int pageSize = 100,
        CancellationToken ct = default);
}