using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceReadService
{
    Task<Result<PagedResult<InvoiceListItemDto>>> GetInvoicesAsync(
        InvoiceListQueryDto query,
        CancellationToken ct = default);

    Task<Result<InvoiceHeadDto>> GetInvoiceDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default);

    Task<List<InvoiceProductVariantSearchItemDto>> SearchProductVariantsAsync(
        string keyword,
        int take = 20,
        CancellationToken ct = default);
}