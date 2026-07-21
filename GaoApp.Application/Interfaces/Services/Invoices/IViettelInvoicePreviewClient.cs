using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Domain.Enums;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IViettelInvoicePreviewClient
{
    Task<Result<ViettelInvoicePreviewFileDto>> CreateDraftPreviewAsync(
        int invoiceHeadId,
        string baseUrl,
        string username,
        string password,
        InvoiceProviderAuthMode authMode,
        string supplierTaxCode,
        ViettelInvoicePayloadDto payload,
        CancellationToken ct = default);
}