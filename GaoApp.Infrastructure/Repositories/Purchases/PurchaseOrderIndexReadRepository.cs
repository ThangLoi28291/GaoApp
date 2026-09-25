using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class PurchaseOrderIndexReadRepository : IPurchaseOrderIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public PurchaseOrderIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PurchaseOrderIndexPageDto> QueryAsync(
        int storeId,
        PurchaseOrderIndexQueryRequest request,
        bool IncludeCost,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyScope(
            BuildBaseQuery(storeId),
            storeId,
            request);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new PurchaseOrderIndexSummaryDto
            {
                TotalItems = group.Count(),
                NeedsActionItems = group.Count(item =>
                    item.Status == PurchaseOrderStatus.Draft
                    || item.Status == PurchaseOrderStatus.ReturnedForRevision
                    || item.Status == PurchaseOrderStatus.PendingApproval),
                InProgressItems = group.Count(item =>
                    item.Status == PurchaseOrderStatus.Approved
                    || item.Status == PurchaseOrderStatus.SentToSupplier
                    || item.Status == PurchaseOrderStatus.PartiallyReceived),
                CompletedItems = group.Count(item =>
                    item.Status == PurchaseOrderStatus.FullyReceived
                    || item.Status == PurchaseOrderStatus.ShortClosed)
            })
            .FirstOrDefaultAsync(ct)
            ?? new PurchaseOrderIndexSummaryDto();

        var filteredQuery = ApplyState(scopedQuery, request.State);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderByDescending(item => item.OrderDate)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new PurchaseOrderIndexItemDto
            {
                OrderId = item.Id,
                OrderNumber = item.OrderNumber,
                Title = item.Title != null && item.Title != string.Empty
                    ? item.Title
                    : item.OrderNumber,
                SourcePurchaseRequestId = item.SourcePurchaseRequestId,
                SourcePurchaseRequestNumber = item.SourcePurchaseRequest != null
                    ? item.SourcePurchaseRequest.RequestNumber
                    : null,
                OrderDate = item.OrderDate,
                ExpectedDeliveryDate = item.ExpectedDeliveryDate,
                SupplierName = item.Supplier != null ? item.Supplier.Name : "Chưa chọn nhà cung cấp",
                SupplierCode = item.Supplier != null ? item.Supplier.Code : string.Empty,
                LegalEntityName = item.LegalEntity.Name,
                WarehouseName = item.ExpectedWarehouse.Name,
                Status = item.Status,
                LineCount = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                EligibleLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0),
                ResolvedLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0
                    && line.ReceivedQuantity + line.ShortClosedQuantity
                        >= line.OrderedQuantity - 0.0005m),
                ReceivingLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0
                    && line.ReceivedQuantity > 0
                    && line.ReceivedQuantity + line.ShortClosedQuantity
                        < line.OrderedQuantity - 0.0005m),
                HasShortClosedLine = item.Lines.Any(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.ShortClosedQuantity > 0),
                TotalAfterVat = IncludeCost ? item.TotalAfterVat : null
            })
            .ToListAsync(ct);

        return new PurchaseOrderIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            CanViewCost = IncludeCost,
            Summary = summary,
            Items = items
        };
    }

    public async Task<PurchaseOrderIndexFilterOptionsDto> GetFilterOptionsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var orders = BuildBaseQuery(storeId);

        var legalEntities = await orders
            .Select(item => new PurchaseOrderIndexOptionDto
            {
                Id = item.LegalEntityId,
                Name = item.LegalEntity.Name,
                Code = item.LegalEntity.Code
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        var suppliers = await orders
            .Where(item => item.SupplierId.HasValue)
            .Select(item => new PurchaseOrderIndexOptionDto
            {
                Id = item.SupplierId!.Value,
                Name = item.Supplier!.Name,
                Code = item.Supplier.Code
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        var warehouses = await orders
            .Select(item => new PurchaseOrderIndexOptionDto
            {
                Id = item.ExpectedWarehouseId,
                Name = item.ExpectedWarehouse.Name,
                Code = item.ExpectedWarehouse.Code,
                LegalEntityId = item.ExpectedWarehouse.LegalEntityId
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .ThenBy(item => item.Id)
            .ToListAsync(ct);

        return new PurchaseOrderIndexFilterOptionsDto
        {
            LegalEntities = legalEntities,
            Suppliers = suppliers,
            Warehouses = warehouses
        };
    }

    public async Task<PurchaseOrderIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int orderId,
        bool IncludeCost,
        CancellationToken ct = default)
    {
        var result = await BuildBaseQuery(storeId)
            .Where(item => item.Id == orderId)
            .Select(item => new PurchaseOrderIndexQuickViewDto
            {
                OrderId = item.Id,
                OrderNumber = item.OrderNumber,
                Title = item.Title != null && item.Title != string.Empty
                    ? item.Title
                    : item.OrderNumber,
                SourcePurchaseRequestNumber = item.SourcePurchaseRequest != null
                    ? item.SourcePurchaseRequest.RequestNumber
                    : null,
                OrderDate = item.OrderDate,
                ExpectedDeliveryDate = item.ExpectedDeliveryDate,
                SupplierName = item.Supplier != null ? item.Supplier.Name : "Chưa chọn nhà cung cấp",
                SupplierCode = item.Supplier != null ? item.Supplier.Code : string.Empty,
                LegalEntityName = item.LegalEntity.Name,
                WarehouseName = item.ExpectedWarehouse.Name,
                Status = item.Status,
                Note = item.Note,
                OutsideRequestReason = item.OutsideRequestReason,
                LineCount = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                EligibleLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0),
                ResolvedLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0
                    && line.ReceivedQuantity + line.ShortClosedQuantity
                        >= line.OrderedQuantity - 0.0005m),
                ReceivingLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.OrderedQuantity > 0
                    && line.ReceivedQuantity > 0
                    && line.ReceivedQuantity + line.ShortClosedQuantity
                        < line.OrderedQuantity - 0.0005m),
                CanViewCost = IncludeCost,
                SubtotalBeforeVat = IncludeCost ? item.SubtotalBeforeVat : null,
                VatTotal = IncludeCost ? item.VatTotal : null,
                TotalAfterVat = IncludeCost ? item.TotalAfterVat : null
            })
            .FirstOrDefaultAsync(ct);

        if (result is null)
            return null;

        result.Lines = await _db.PurchaseOrderLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && line.PurchaseOrderId == orderId
                && line.PurchaseOrder.StoreId == storeId
                && !line.PurchaseOrder.IsDeleted)
            .OrderBy(line => line.LineNo)
            .Select(line => new PurchaseOrderIndexLineDto
            {
                LineNo = line.LineNo,
                ProductName = line.ProductNameSnapshot,
                Sku = line.SkuSnapshot,
                Barcode = line.ProductUnitConversionId.HasValue
                    ? _db.ProductVariantUnitBarcodes
                        .Where(barcode =>
                            barcode.StoreId == storeId
                            && !barcode.IsDeleted
                            && barcode.IsActive
                            && barcode.ProductUnitConversionId == line.ProductUnitConversionId.Value)
                        .OrderByDescending(barcode => barcode.IsPrimary)
                        .ThenBy(barcode => barcode.Id)
                        .Select(barcode => barcode.Barcode)
                        .FirstOrDefault()
                    : null,
                ImageUrl = line.ProductVariant != null
                    && line.ProductVariant.StoreId == storeId
                    && !line.ProductVariant.IsDeleted
                    && line.ProductVariant.PrimaryProductImage != null
                    && !line.ProductVariant.PrimaryProductImage.IsDeleted
                        ? line.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                        : line.ProductVariant != null
                            ? line.ProductVariant.Product.ProductImages
                                .Where(image =>
                                    image.StoreId == storeId && !image.IsDeleted)
                                .OrderByDescending(image => image.IsPrimary)
                                .ThenBy(image => image.SortOrder)
                                .ThenBy(image => image.Id)
                                .Select(image => image.MediaAsset.StoragePath)
                                .FirstOrDefault()
                            : null,
                UnitName = line.UnitNameSnapshot,
                OrderedQuantity = line.OrderedQuantity,
                ReceivedQuantity = line.ReceivedQuantity,
                ShortClosedQuantity = line.ShortClosedQuantity,
                RemainingQuantity = line.OrderedQuantity
                    - line.ReceivedQuantity
                    - line.ShortClosedQuantity,
                IsResolved = line.OrderedQuantity > 0
                    && line.ReceivedQuantity + line.ShortClosedQuantity
                        >= line.OrderedQuantity - 0.0005m,
                UnitPriceBeforeVat = IncludeCost ? line.UnitPriceBeforeVat : null,
                TaxRate = IncludeCost ? line.TaxRate : null,
                LineTotalAfterVat = IncludeCost ? line.LineTotalAfterVat : null
            })
            .ToListAsync(ct);

        foreach (var line in result.Lines)
        {
            line.RemainingQuantity = Math.Max(0, line.RemainingQuantity);
            line.ImageUrl = NormalizeImageUrl(line.ImageUrl);
        }

        return result;
    }

    private IQueryable<PurchaseOrder> BuildBaseQuery(int storeId)
        => _db.PurchaseOrders
            .AsNoTracking()
            .Where(item => item.StoreId == storeId && !item.IsDeleted);

    private IQueryable<PurchaseOrder> ApplyScope(
        IQueryable<PurchaseOrder> query,
        int storeId,
        PurchaseOrderIndexQueryRequest request)
    {
        if (request.LegalEntityId.HasValue)
            query = query.Where(item => item.LegalEntityId == request.LegalEntityId.Value);

        if (request.SupplierId.HasValue)
            query = query.Where(item => item.SupplierId == request.SupplierId.Value);

        if (request.WarehouseId.HasValue)
            query = query.Where(item => item.ExpectedWarehouseId == request.WarehouseId.Value);

        query = request.Source switch
        {
            PurchaseOrderIndexSources.Request => query.Where(item =>
                item.SourcePurchaseRequestId.HasValue),
            PurchaseOrderIndexSources.Direct => query.Where(item =>
                !item.SourcePurchaseRequestId.HasValue),
            _ => query
        };

        if (request.FromDate.HasValue)
        {
            var fromDate = request.FromDate.Value.Date;
            query = query.Where(item => item.OrderDate >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDateExclusive = request.ToDate.Value.Date.AddDays(1);
            query = query.Where(item => item.OrderDate < toDateExclusive);
        }

        return ApplyKeyword(query, storeId, request.Keyword);
    }

    private IQueryable<PurchaseOrder> ApplyKeyword(
        IQueryable<PurchaseOrder> query,
        int storeId,
        string? keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return query;

        var search = keyword.Trim();
        if (_db.Database.IsRelational())
        {
            var accentInsensitiveSearch = search
                .Replace('Đ', 'D')
                .Replace('đ', 'd');

            return query.Where(item =>
                item.OrderNumber.Contains(search)
                || (item.Title != null
                    && EF.Functions.Collate(
                        item.Title.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch))
                || (item.Supplier != null
                    && EF.Functions.Collate(
                        item.Supplier.Name.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch))
                || (item.Supplier != null && item.Supplier.Code != null
                    && item.Supplier.Code.Contains(search))
                || (item.Supplier != null && item.Supplier.TaxCode != null
                    && item.Supplier.TaxCode.Contains(search))
                || (item.SourcePurchaseRequest != null
                    && item.SourcePurchaseRequest.RequestNumber.Contains(search))
                || item.Lines.Any(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && (EF.Functions.Collate(
                            line.ProductNameSnapshot.Replace("Đ", "D").Replace("đ", "d"),
                            AccentInsensitiveSearchCollation)
                            .Contains(accentInsensitiveSearch)
                        || (line.SkuSnapshot != null
                            && EF.Functions.Collate(
                                line.SkuSnapshot.Replace("Đ", "D").Replace("đ", "d"),
                                AccentInsensitiveSearchCollation)
                                .Contains(accentInsensitiveSearch))
                        || (line.ProductUnitConversionId.HasValue
                            && _db.ProductVariantUnitBarcodes.Any(barcode =>
                                barcode.StoreId == storeId
                                && !barcode.IsDeleted
                                && barcode.IsActive
                                && barcode.ProductUnitConversionId == line.ProductUnitConversionId.Value
                                && barcode.Barcode.Contains(search))))));
        }

        return query.Where(item =>
            item.OrderNumber.Contains(search)
            || (item.Title != null && item.Title.Contains(search))
            || (item.Supplier != null && item.Supplier.Name.Contains(search))
            || (item.Supplier != null && item.Supplier.Code != null && item.Supplier.Code.Contains(search))
            || (item.Supplier != null && item.Supplier.TaxCode != null && item.Supplier.TaxCode.Contains(search))
            || (item.SourcePurchaseRequest != null
                && item.SourcePurchaseRequest.RequestNumber.Contains(search))
            || item.Lines.Any(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && (line.ProductNameSnapshot.Contains(search)
                    || (line.SkuSnapshot != null && line.SkuSnapshot.Contains(search))
                    || (line.ProductUnitConversionId.HasValue
                        && _db.ProductVariantUnitBarcodes.Any(barcode =>
                            barcode.StoreId == storeId
                            && !barcode.IsDeleted
                            && barcode.IsActive
                            && barcode.ProductUnitConversionId == line.ProductUnitConversionId.Value
                            && barcode.Barcode.Contains(search))))));
    }

    private static IQueryable<PurchaseOrder> ApplyState(
        IQueryable<PurchaseOrder> query,
        string? state)
        => state switch
        {
            PurchaseOrderIndexStates.Open => query.Where(item =>
                item.Status == PurchaseOrderStatus.Draft
                || item.Status == PurchaseOrderStatus.ReturnedForRevision
                || item.Status == PurchaseOrderStatus.PendingApproval
                || item.Status == PurchaseOrderStatus.Approved
                || item.Status == PurchaseOrderStatus.SentToSupplier
                || item.Status == PurchaseOrderStatus.PartiallyReceived),
            PurchaseOrderIndexStates.NeedsAction => query.Where(item =>
                item.Status == PurchaseOrderStatus.Draft
                || item.Status == PurchaseOrderStatus.ReturnedForRevision
                || item.Status == PurchaseOrderStatus.PendingApproval),
            PurchaseOrderIndexStates.InProgress => query.Where(item =>
                item.Status == PurchaseOrderStatus.Approved
                || item.Status == PurchaseOrderStatus.SentToSupplier
                || item.Status == PurchaseOrderStatus.PartiallyReceived),
            PurchaseOrderIndexStates.Completed => query.Where(item =>
                item.Status == PurchaseOrderStatus.FullyReceived
                || item.Status == PurchaseOrderStatus.ShortClosed),
            PurchaseOrderIndexStates.Draft => query.Where(item =>
                item.Status == PurchaseOrderStatus.Draft),
            PurchaseOrderIndexStates.Pending => query.Where(item =>
                item.Status == PurchaseOrderStatus.PendingApproval),
            PurchaseOrderIndexStates.Returned => query.Where(item =>
                item.Status == PurchaseOrderStatus.ReturnedForRevision),
            PurchaseOrderIndexStates.Rejected => query.Where(item =>
                item.Status == PurchaseOrderStatus.Rejected),
            PurchaseOrderIndexStates.Approved => query.Where(item =>
                item.Status == PurchaseOrderStatus.Approved),
            PurchaseOrderIndexStates.Sent => query.Where(item =>
                item.Status == PurchaseOrderStatus.SentToSupplier),
            PurchaseOrderIndexStates.Receiving => query.Where(item =>
                item.Status == PurchaseOrderStatus.PartiallyReceived),
            PurchaseOrderIndexStates.FullyReceived => query.Where(item =>
                item.Status == PurchaseOrderStatus.FullyReceived),
            PurchaseOrderIndexStates.ShortClosed => query.Where(item =>
                item.Status == PurchaseOrderStatus.ShortClosed),
            PurchaseOrderIndexStates.Cancelled => query.Where(item =>
                item.Status == PurchaseOrderStatus.Cancelled),
            _ => query
        };

    private static string? NormalizeImageUrl(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
            return null;

        var normalized = storagePath.Trim();
        if (normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        return "/" + normalized.TrimStart('/');
    }
}
