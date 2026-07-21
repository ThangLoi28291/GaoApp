using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;

namespace GaoApp.Application.Interfaces.Services.Invoices;

public interface IInvoiceService
{
    Task<Result<InvoiceHeadDto>> CreateInvoiceHeadFromOrderAsync(
        int orderId,
        CancellationToken ct = default);

    Task<Result<InvoiceHeadDto>> GenerateDetailsFromOrderLinesAsync(
        int orderId,
        CancellationToken ct = default);
    Task<Result<List<InvoiceHeadDto>>> GenerateInvoicesFromOrderAsync(
        int orderId,
        CancellationToken ct = default);
    Task<Result<InvoiceHeadDto>> AddManualDetailAsync(
    CreateManualInvoiceDetailRequest request,
    CancellationToken ct = default);
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
    Task<Result<InvoiceHeadDto>> DeleteManualDetailAsync(
    int invoiceDetailId,
    CancellationToken ct = default);
    Task<Result<InvoiceHeadDto>> UpdateManualDetailAsync(
    UpdateManualInvoiceDetailRequest request,
    CancellationToken ct = default);
    Task<Result<InvoiceHeadDto>> LockInvoiceAsync(
    LockInvoiceRequest request,
    CancellationToken ct = default);

    Task<Result<InvoiceHeadDto>> UnlockInvoiceAsync(
        UnlockInvoiceRequest request,
        CancellationToken ct = default);
    Task<Result<InvoiceHeadDto>> UpdateBuyerInfoAsync(
     UpdateInvoiceBuyerInfoRequest request,
     CancellationToken ct = default);

    Task<Result<InvoiceBuyerLookupDto>> LookupBuyerByTaxCodeAsync(
        int invoiceHeadId,
        string buyerType,
        string taxCode,
        CancellationToken ct = default);

}
