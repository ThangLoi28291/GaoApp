using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoiceEmailClient
{
    Task<Result<ViettelInvoiceSendEmailResultDto>> SendEmailToCustomerAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        string transactionUuid,
        string buyerEmail,
        string? providerInvoiceNo,
        CancellationToken ct = default);
}