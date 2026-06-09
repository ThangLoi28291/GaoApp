using GaoApp.Application.DTOs.Products.BarcodeVerification;
using GaoApp.Application.Interfaces.Repositories.Products;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Products;

public sealed class ProductBarcodeVerificationRepository : IProductBarcodeVerificationRepository
{
    private readonly AppDbContext _db;

    public ProductBarcodeVerificationRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<List<MissingBarcodeUnitDto>> GetMissingUnitsForStockDocumentAsync(
        int stockDocumentId,
        int storeId,
        CancellationToken ct = default)
    {
        var stockDocumentExists = await _db.StockDocuments
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == stockDocumentId &&
                x.StoreId == storeId &&
                !x.IsDeleted,
                ct);

        if (!stockDocumentExists)
            return new List<MissingBarcodeUnitDto>();

        var variantIds = await _db.StockDocumentLines
            .AsNoTracking()
            .Where(x => x.StockDocumentId == stockDocumentId)
            .Select(x => x.ProductVariantId)
            .Distinct()
            .ToListAsync(ct);

        if (!variantIds.Any())
            return new List<MissingBarcodeUnitDto>();

        var handledStatuses = new[]
        {
            BarcodeVerificationRequestStatus.Pending,
            BarcodeVerificationRequestStatus.Approved,
            BarcodeVerificationRequestStatus.ConfirmedNoBarcode
        };

        var handledConversionIds = await _db.ProductBarcodeVerificationRequests
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                handledStatuses.Contains(x.Status))
            .Select(x => x.ProductUnitConversionId)
            .Distinct()
            .ToListAsync(ct);

        var conversions = await _db.ProductUnitConversions
            .AsNoTracking()
            .Include(x => x.Unit)
            .Include(x => x.Barcodes)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                !x.IsBaseUnit &&
                variantIds.Contains(x.ProductVariantId) &&
                !handledConversionIds.Contains(x.Id))
            .ToListAsync(ct);

        var result = new List<MissingBarcodeUnitDto>();

        foreach (var conversion in conversions)
        {
            var activeBarcodes = conversion.Barcodes
                .Where(x => x.IsActive && !x.IsDeleted)
                .ToList();

            var hasLegacy = activeBarcodes.Any(x =>
                x.BarcodeType == BarcodeType.Legacy);

            var hasStandardBarcode = activeBarcodes.Any(x =>
                x.BarcodeType == BarcodeType.Supplier ||
                x.BarcodeType == BarcodeType.External ||
                x.BarcodeType == BarcodeType.Packaging);

            if (!hasLegacy || hasStandardBarcode)
                continue;

            result.Add(new MissingBarcodeUnitDto
            {
                ProductVariantId = conversion.ProductVariantId,
                ProductUnitConversionId = conversion.Id,
                ProductName =
                    conversion.ProductVariant.ProductVariantName
                    ?? conversion.ProductVariant.Product.Name,
                UnitName = conversion.Unit.Name,
                Factor = conversion.Factor,
                LegacyBarcode = activeBarcodes
                    .FirstOrDefault(x => x.BarcodeType == BarcodeType.Legacy)
                    ?.Barcode,
                ImageUrl = null
            });
        }

        return result
            .OrderBy(x => x.ProductName)
            .ThenBy(x => x.Factor)
            .ToList();
    }

    public async Task<List<ProductUnitConversion>> GetValidConversionsForRequestAsync(
        int stockDocumentId,
        int storeId,
        List<int> productUnitConversionIds,
        CancellationToken ct = default)
    {
        var stockDocumentExists = await _db.StockDocuments
            .AsNoTracking()
            .AnyAsync(x =>
                x.Id == stockDocumentId &&
                x.StoreId == storeId &&
                !x.IsDeleted,
                ct);

        if (!stockDocumentExists)
            return new List<ProductUnitConversion>();

        var variantIdsInDocument = await _db.StockDocumentLines
            .AsNoTracking()
            .Where(x => x.StockDocumentId == stockDocumentId)
            .Select(x => x.ProductVariantId)
            .Distinct()
            .ToListAsync(ct);

        return await _db.ProductUnitConversions
            .Include(x => x.Unit)
            .Include(x => x.ProductVariant)
                .ThenInclude(x => x.Product)
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive &&
                productUnitConversionIds.Contains(x.Id) &&
                variantIdsInDocument.Contains(x.ProductVariantId))
            .ToListAsync(ct);
    }

    public Task<List<int>> GetHandledConversionIdsAsync(
        int storeId,
        List<int> productUnitConversionIds,
        CancellationToken ct = default)
    {
        return _db.ProductBarcodeVerificationRequests
            .AsNoTracking()
            .Where(x =>
                x.StoreId == storeId &&
                !x.IsDeleted &&
                productUnitConversionIds.Contains(x.ProductUnitConversionId) &&
                (
                    x.Status == BarcodeVerificationRequestStatus.Pending ||
                    x.Status == BarcodeVerificationRequestStatus.Approved ||
                    x.Status == BarcodeVerificationRequestStatus.ConfirmedNoBarcode
                ))
            .Select(x => x.ProductUnitConversionId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<bool> BarcodeExistsAsync(
        int storeId,
        string barcode,
        CancellationToken ct = default)
    {
        return _db.ProductVariantUnitBarcodes
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x =>
                x.StoreId == storeId &&
                x.Barcode == barcode,
                ct);
    }

    public async Task AddRequestsAsync(
        List<ProductBarcodeVerificationRequest> requests,
        CancellationToken ct = default)
    {
        await _db.ProductBarcodeVerificationRequests.AddRangeAsync(requests, ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        return _db.SaveChangesAsync(ct);
    }
    public async Task<List<BarcodeVerificationManagementDto>> GetManagementListAsync(
    int storeId,
    BarcodeVerificationRequestStatus? status,
    string? keyword,
    CancellationToken ct = default)
    {
        keyword = keyword?.Trim();

        var q = _db.ProductBarcodeVerificationRequests
            .AsNoTracking()
            .Where(x => x.StoreId == storeId && !x.IsDeleted);

        if (status.HasValue)
        {
            q = q.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            q = q.Where(x =>
                x.ProductNameSnapshot.Contains(keyword) ||
                x.UnitNameSnapshot.Contains(keyword) ||
                (x.SuggestedBarcode != null && x.SuggestedBarcode.Contains(keyword)));
        }

        var items = await q
            .OrderBy(x => x.Status)
            .ThenByDescending(x => x.RequestedAtUtc)
            .Select(x => new BarcodeVerificationManagementDto
            {
                Id = x.Id,
                ProductVariantId = x.ProductVariantId,
                ProductUnitConversionId = x.ProductUnitConversionId,
                StockDocumentId = x.StockDocumentId,

                ProductNameSnapshot = x.ProductNameSnapshot,
                UnitNameSnapshot = x.UnitNameSnapshot,
                FactorSnapshot = x.FactorSnapshot,
                SuggestedBarcode = x.SuggestedBarcode,

                RequestType = x.RequestType,
                Status = x.Status,

                EmployeeNote = x.EmployeeNote,
                ManagerNote = x.ManagerNote,

                RequestedByUserId = x.RequestedByUserId,
                RequestedAtUtc = x.RequestedAtUtc,

                ResolvedByUserId = x.ResolvedByUserId,
                ResolvedAtUtc = x.ResolvedAtUtc,

                CreatedBarcodeId = x.CreatedBarcodeId,

                StockDocumentNo = null,
                WarehouseName = null
            })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            item.RequestTypeText = item.RequestType switch
            {
                BarcodeVerificationRequestType.SupplierBarcode => "Đề xuất mã NCC/NSX",
                BarcodeVerificationRequestType.NoSupplierBarcode => "Xác nhận không có mã",
                _ => item.RequestType.ToString()
            };

            item.StatusText = item.Status switch
            {
                BarcodeVerificationRequestStatus.Pending => "Chờ xử lý",
                BarcodeVerificationRequestStatus.Approved => "Đã duyệt",
                BarcodeVerificationRequestStatus.Rejected => "Từ chối",
                BarcodeVerificationRequestStatus.ConfirmedNoBarcode => "Đã xác nhận không có mã",
                _ => item.Status.ToString()
            };
        }

        return items;
    }

    public Task<ProductBarcodeVerificationRequest?> GetRequestForUpdateAsync(
        int storeId,
        int id,
        CancellationToken ct = default)
    {
        return _db.ProductBarcodeVerificationRequests
            .FirstOrDefaultAsync(x =>
                x.Id == id &&
                x.StoreId == storeId &&
                !x.IsDeleted,
                ct);
    }

    public Task<ProductUnitConversion?> GetConversionForBarcodeCreateAsync(
        int storeId,
        int productUnitConversionId,
        CancellationToken ct = default)
    {
        return _db.ProductUnitConversions
            .Include(x => x.Barcodes)
            .FirstOrDefaultAsync(x =>
                x.Id == productUnitConversionId &&
                x.StoreId == storeId &&
                !x.IsDeleted &&
                x.IsActive,
                ct);
    }

    public async Task AddBarcodeAsync(
        ProductVariantUnitBarcode barcode,
        CancellationToken ct = default)
    {
        await _db.ProductVariantUnitBarcodes.AddAsync(barcode, ct);
    }
}