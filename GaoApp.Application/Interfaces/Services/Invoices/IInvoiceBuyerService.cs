using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceBuyerService
{
    Task<Result<InvoiceBuyerLookupDto>> LookupBuyerByTaxCodeAsync(
        int invoiceHeadId,
        string buyerType,
        string taxCode,
        CancellationToken ct = default);

    Task<Result<InvoiceHeadDto>> UpdateBuyerInfoAsync(
        UpdateInvoiceBuyerInfoRequest request,
        CancellationToken ct = default);
}