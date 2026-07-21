using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceService : IInvoiceService
{
    private readonly IInvoiceReadService _readService;
    private readonly IInvoiceCommandService _commandService;
    private readonly IInvoiceBuyerService _buyerService;

    public InvoiceService(
        IInvoiceReadService readService,
        IInvoiceCommandService commandService,
        IInvoiceBuyerService buyerService)
    {
        _readService = readService;
        _commandService = commandService;
        _buyerService = buyerService;
    }

    public Task<Result<InvoiceHeadDto>> CreateInvoiceHeadFromOrderAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _commandService.CreateInvoiceHeadFromOrderAsync(orderId, ct);
    }

    public Task<Result<InvoiceHeadDto>> GenerateDetailsFromOrderLinesAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _commandService.GenerateDetailsFromOrderLinesAsync(orderId, ct);
    }

    public Task<Result<List<InvoiceHeadDto>>> GenerateInvoicesFromOrderAsync(
        int orderId,
        CancellationToken ct = default)
    {
        return _commandService.GenerateInvoicesFromOrderAsync(orderId, ct);
    }

    public Task<Result<InvoiceHeadDto>> AddManualDetailAsync(
        CreateManualInvoiceDetailRequest request,
        CancellationToken ct = default)
    {
        return _commandService.AddManualDetailAsync(request, ct);
    }

    public Task<Result<PagedResult<InvoiceListItemDto>>> GetInvoicesAsync(
        InvoiceListQueryDto query,
        CancellationToken ct = default)
    {
        return _readService.GetInvoicesAsync(query, ct);
    }

    public Task<Result<InvoiceHeadDto>> GetInvoiceDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        return _readService.GetInvoiceDetailAsync(invoiceHeadId, ct);
    }

    public Task<List<InvoiceProductVariantSearchItemDto>> SearchProductVariantsAsync(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
    {
        return _readService.SearchProductVariantsAsync(keyword, take, ct);
    }

    public Task<Result<InvoiceHeadDto>> DeleteManualDetailAsync(
        int invoiceDetailId,
        CancellationToken ct = default)
    {
        return _commandService.DeleteManualDetailAsync(invoiceDetailId, ct);
    }

    public Task<Result<InvoiceHeadDto>> UpdateManualDetailAsync(
        UpdateManualInvoiceDetailRequest request,
        CancellationToken ct = default)
    {
        return _commandService.UpdateManualDetailAsync(request, ct);
    }

    public Task<Result<InvoiceHeadDto>> LockInvoiceAsync(
        LockInvoiceRequest request,
        CancellationToken ct = default)
    {
        return _commandService.LockInvoiceAsync(request, ct);
    }

    public Task<Result<InvoiceHeadDto>> UnlockInvoiceAsync(
        UnlockInvoiceRequest request,
        CancellationToken ct = default)
    {
        return _commandService.UnlockInvoiceAsync(request, ct);
    }

    public Task<Result<InvoiceBuyerLookupDto>> LookupBuyerByTaxCodeAsync(
        int invoiceHeadId,
        string buyerType,
        string taxCode,
        CancellationToken ct = default)
    {
        return _buyerService.LookupBuyerByTaxCodeAsync(
            invoiceHeadId,
            buyerType,
            taxCode,
            ct);
    }

    public Task<Result<InvoiceHeadDto>> UpdateBuyerInfoAsync(
        UpdateInvoiceBuyerInfoRequest request,
        CancellationToken ct = default)
    {
        return _buyerService.UpdateBuyerInfoAsync(request, ct);
    }
}
