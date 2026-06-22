using GaoApp.Application.Common;
using GaoApp.Application.Common.Results;
using GaoApp.Application.DTOs.Invoices;
using GaoApp.Application.Interfaces.Repositories.Invoices;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Invoices;

namespace GaoApp.Application.Services.Invoices;

public class InvoiceReadService : IInvoiceReadService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IProductVariantRepository _productVariantRepository;

    public InvoiceReadService(
        IInvoiceRepository invoiceRepository,
        IProductVariantRepository productVariantRepository)
    {
        _invoiceRepository = invoiceRepository;
        _productVariantRepository = productVariantRepository;
    }

    public async Task<Result<PagedResult<InvoiceListItemDto>>> GetInvoicesAsync(
        InvoiceListQueryDto query,
        CancellationToken ct = default)
    {
        query ??= new InvoiceListQueryDto();

        var page = query.Page <= 0 ? 1 : query.Page;
        var pageSize = query.PageSize <= 0 ? 20 : query.PageSize;

        var (items, total) = await _invoiceRepository.QueryInvoiceHeadsAsync(
            query.FromDate,
            query.ToDate,
            query.OrderId,
            query.Keyword,
            query.DisplayMode,
            query.SortMode,
            page,
            pageSize,
            ct);

        var dtoItems = items
            .Select(InvoiceDtoMapper.ToListItemDto)
            .ToList();

        return Result<PagedResult<InvoiceListItemDto>>.Success(
            new PagedResult<InvoiceListItemDto>
            {
                Items = dtoItems,
                Page = page,
                PageSize = pageSize,
                TotalItems = total
            });
    }

    public async Task<Result<InvoiceHeadDto>> GetInvoiceDetailAsync(
        int invoiceHeadId,
        CancellationToken ct = default)
    {
        if (invoiceHeadId <= 0)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.Validation(
                    "Invoice.InvalidInvoiceHeadId",
                    "InvoiceHeadId không hợp lệ."));
        }

        var invoice = await _invoiceRepository.GetInvoiceHeadDetailAsync(
            invoiceHeadId,
            ct);

        if (invoice == null)
        {
            return Result<InvoiceHeadDto>.Failure(
                Error.NotFound("Không tìm thấy hóa đơn bán ra."));
        }

        return Result<InvoiceHeadDto>.Success(
            InvoiceDtoMapper.ToDetailDto(invoice));
    }

    public async Task<List<InvoiceProductVariantSearchItemDto>> SearchProductVariantsAsync(
        string keyword,
        int take = 20,
        CancellationToken ct = default)
    {
        keyword = (keyword ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return new List<InvoiceProductVariantSearchItemDto>();

        var variants = await _productVariantRepository.SearchForPOSAsync(
            keyword,
            take,
            ct);

        return variants
            .Select(x =>
            {
                var productName = x.Product?.Name ?? string.Empty;
                var variantName = x.ProductVariantName ?? string.Empty;

                var displayName = string.IsNullOrWhiteSpace(variantName)
                    ? productName
                    : variantName;

                var unitName = x.Product?.BaseUnit?.Name;

                return new InvoiceProductVariantSearchItemDto
                {
                    ProductVariantId = x.Id,
                    ProductId = x.ProductId,
                    DisplayName = displayName,
                    Sku = x.Sku,
                    Barcode = null,
                    UnitName = unitName,
                    UnitPrice = x.Price ?? x.Product?.BasePrice ?? 0m
                };
            })
            .ToList();
    }
}