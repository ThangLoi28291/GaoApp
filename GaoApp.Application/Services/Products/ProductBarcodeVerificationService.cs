using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Application.Interfaces.Services.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;

using GaoApp.Application.Common.Exceptions;

namespace GaoApp.Infrastructure.Services.Products;

public class ProductBarcodeVerificationService : IProductBarcodeVerificationService
{
    private readonly IProductBarcodeVerificationRepository _repository;
    private readonly IReceiptBarcodeProposalService _proposals;

    public ProductBarcodeVerificationService(
        IProductBarcodeVerificationRepository repository, IReceiptBarcodeProposalService proposals)
    {
        _repository = repository;
        _proposals = proposals;
    }

    public Task<List<MissingBarcodeUnitDto>> GetMissingUnitsForStockDocumentAsync(
        int stockDocumentId,
        int storeId,
        CancellationToken ct = default)
    {
        return _repository.GetMissingUnitsForStockDocumentAsync(stockDocumentId, storeId, ct);
    }

    public async Task<int> SubmitRequestsAsync(
        int stockDocumentId,
        int storeId,
        int userId,
        SubmitBarcodeVerificationRequest request,
        CancellationToken ct = default)
    {
        if (request.Items == null || !request.Items.Any())
            return 0;

        var items = request.Items
            .Where(x => x.ProductUnitConversionId > 0)
            .Select(x => new
            {
                x.ProductUnitConversionId,
                SuggestedBarcode = NormalizeBarcode(x.SuggestedBarcode),
                x.NoSupplierBarcode,
                Note = x.Note?.Trim()
            })
            .Where(x => x.NoSupplierBarcode || !string.IsNullOrWhiteSpace(x.SuggestedBarcode))
            .GroupBy(x => x.ProductUnitConversionId)
            .Select(x => x.First())
            .ToList();

        if (!items.Any())
            return 0;

        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.SuggestedBarcode)))
        {
            var exists = await _repository.BarcodeExistsAsync(storeId, item.SuggestedBarcode!, ct);

            if (exists)
                throw new BusinessRuleException($"Barcode {item.SuggestedBarcode} đã tồn tại.");
        }

        var conversionIds = items.Select(x => x.ProductUnitConversionId).ToList();

        var handledIds = await _repository.GetHandledConversionIdsAsync(storeId, conversionIds, ct);

        var conversions = await _repository.GetValidConversionsForRequestAsync(
            stockDocumentId,
            storeId,
            conversionIds,
            ct);

        var now = DateTime.UtcNow;
        var entities = new List<ProductBarcodeVerificationRequest>();

        foreach (var item in items)
        {
            if (handledIds.Contains(item.ProductUnitConversionId))
                continue;

            var conversion = conversions.FirstOrDefault(x => x.Id == item.ProductUnitConversionId);
            if (conversion == null)
                continue;

            entities.Add(new ProductBarcodeVerificationRequest
            {
                StoreId = storeId,
                StockDocumentId = stockDocumentId,
                ProductVariantId = conversion.ProductVariantId,
                ProductUnitConversionId = conversion.Id,
                ProductNameSnapshot =
                    conversion.ProductVariant.ProductVariantName
                    ?? conversion.ProductVariant.Product.Name,
                UnitNameSnapshot = conversion.Unit.Name,
                FactorSnapshot = conversion.Factor,
                SuggestedBarcode = item.NoSupplierBarcode ? null : item.SuggestedBarcode,
                RequestType = item.NoSupplierBarcode
                    ? BarcodeVerificationRequestType.NoSupplierBarcode
                    : BarcodeVerificationRequestType.SupplierBarcode,
                Status = BarcodeVerificationRequestStatus.Pending,
                EmployeeNote = item.Note,
                RequestedByUserId = userId,
                RequestedAtUtc = now,
                IsDeleted = false
            });
        }

        if (!entities.Any())
            return 0;

        await _repository.AddRequestsAsync(entities, ct);
        await _repository.SaveChangesAsync(ct);

        return entities.Count;
    }
    public Task<List<BarcodeVerificationManagementDto>> GetManagementListAsync(
    int storeId,
    BarcodeVerificationRequestStatus? status,
    string? keyword,
    CancellationToken ct = default)
    {
        return _repository.GetManagementListAsync(storeId, status, keyword, ct);
    }

    public Task ApproveAsync(int storeId, int requestId, int managerUserId, string? managerNote, CancellationToken ct = default)
        => _proposals.ResolveAsync(storeId, null, requestId, managerUserId, BarcodeVerificationRequestStatus.Approved, managerNote, ct);

    public Task RejectAsync(int storeId, int requestId, int managerUserId, string? managerNote, CancellationToken ct = default)
        => _proposals.ResolveAsync(storeId, null, requestId, managerUserId, BarcodeVerificationRequestStatus.Rejected, managerNote, ct);

    public Task ConfirmNoBarcodeAsync(int storeId, int requestId, int managerUserId, string? managerNote, CancellationToken ct = default)
        => _proposals.ResolveAsync(storeId, null, requestId, managerUserId, BarcodeVerificationRequestStatus.ConfirmedNoBarcode, managerNote, ct);
    private static string? NormalizeBarcode(string? barcode)
    {
        barcode = barcode?.Trim();
        return string.IsNullOrWhiteSpace(barcode)
            ? null
            : barcode.Replace(" ", "");
    }
}