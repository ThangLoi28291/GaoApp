using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceAuthClient
{
    Task<Result<TestInvoiceProviderLoginResultDto>> TestConnectionAsync(
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string invoiceType,
        string templateCode,
        string invoiceSeries,
        CancellationToken ct = default);
}