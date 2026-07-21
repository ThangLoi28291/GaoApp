using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceLookupClient
{
    Task<Result<ViettelInvoiceLookupResultDto>> SearchByTransactionUuidAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string transactionUuid,
        CancellationToken ct = default);
}