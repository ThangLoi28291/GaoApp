using GaoApp.Application.DTOs.Purchases;
using GaoApp.Application.Interfaces.Repositories.Purchases;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Purchases;

public sealed class PurchaseRequestIndexReadRepository : IPurchaseRequestIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public PurchaseRequestIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<PurchaseRequestIndexPageDto> QueryAsync(
        int storeId,
        int? requestedByUserId,
        PurchaseRequestIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyScope(
            BuildBaseQuery(storeId),
            storeId,
            requestedByUserId,
            request);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new PurchaseRequestIndexSummaryDto
            {
                TotalItems = group.Count(),
                WorkingItems = group.Count(item =>
                    item.Status == PurchaseRequestStatus.Draft
                    || item.Status == PurchaseRequestStatus.ReturnedForRevision
                    || item.Status == PurchaseRequestStatus.PendingApproval),
                OrderReadyItems = group.Count(item =>
                    item.Status == PurchaseRequestStatus.Approved
                    || item.Status == PurchaseRequestStatus.PartiallyConverted),
                ConvertedItems = group.Count(item =>
                    item.Status == PurchaseRequestStatus.Converted)
            })
            .FirstOrDefaultAsync(ct)
            ?? new PurchaseRequestIndexSummaryDto();

        var filteredQuery = ApplyState(scopedQuery, request.State);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderByDescending(item => item.RequestDate)
            .ThenByDescending(item => item.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new PurchaseRequestIndexItemDto
            {
                RequestId = item.Id,
                RequestNumber = item.RequestNumber,
                Title = item.Title,
                RequestDate = item.RequestDate,
                NeedByDate = item.NeedByDate,
                Status = item.Status,
                RequestedByName = _db.Users
                    .Where(user => user.Id == item.RequestedByUserId && !user.IsDeleted)
                    .Select(user => user.FullName != null && user.FullName != string.Empty
                        ? user.FullName
                        : user.UserName)
                    .FirstOrDefault() ?? "Người dùng",
                LineCount = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                EligibleLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.ApprovedQuantity.HasValue
                    && line.ApprovedQuantity.Value > 0),
                CompletedLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.ApprovedQuantity.HasValue
                    && line.ApprovedQuantity.Value > 0
                    && line.ConvertedQuantity >= line.ApprovedQuantity.Value)
            })
            .ToListAsync(ct);

        return new PurchaseRequestIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    public async Task<List<PurchaseRequestIndexRequesterOptionDto>> GetRequesterOptionsAsync(
        int storeId,
        CancellationToken ct = default)
    {
        var requesterIds = BuildBaseQuery(storeId)
            .Select(item => item.RequestedByUserId)
            .Distinct();

        return await _db.Users
            .AsNoTracking()
            .Where(user => !user.IsDeleted && requesterIds.Contains(user.Id))
            .OrderBy(user => user.FullName ?? user.UserName)
            .ThenBy(user => user.Id)
            .Select(user => new PurchaseRequestIndexRequesterOptionDto
            {
                UserId = user.Id,
                Name = user.FullName != null && user.FullName != string.Empty
                    ? user.FullName
                    : user.UserName
            })
            .ToListAsync(ct);
    }

    public async Task<PurchaseRequestIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int requestId,
        int? requestedByUserId,
        CancellationToken ct = default)
    {
        var query = BuildBaseQuery(storeId)
            .Where(item => item.Id == requestId);

        if (requestedByUserId.HasValue)
        {
            query = query.Where(item =>
                item.RequestedByUserId == requestedByUserId.Value);
        }

        var result = await query
            .Select(item => new PurchaseRequestIndexQuickViewDto
            {
                RequestId = item.Id,
                RequestNumber = item.RequestNumber,
                Title = item.Title,
                RequestDate = item.RequestDate,
                NeedByDate = item.NeedByDate,
                RequestedByName = _db.Users
                    .Where(user => user.Id == item.RequestedByUserId && !user.IsDeleted)
                    .Select(user => user.FullName != null && user.FullName != string.Empty
                        ? user.FullName
                        : user.UserName)
                    .FirstOrDefault() ?? "Người dùng",
                Status = item.Status,
                Note = item.Note,
                LineCount = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                EligibleLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.ApprovedQuantity.HasValue
                    && line.ApprovedQuantity.Value > 0),
                CompletedLineCount = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.ApprovedQuantity.HasValue
                    && line.ApprovedQuantity.Value > 0
                    && line.ConvertedQuantity >= line.ApprovedQuantity.Value)
            })
            .FirstOrDefaultAsync(ct);

        if (result is null)
            return null;

        result.Lines = await _db.PurchaseRequestLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && line.PurchaseRequestId == requestId)
            .OrderBy(line => line.LineNo)
            .Select(line => new PurchaseRequestIndexLineDto
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
                RequestedQuantity = line.RequestedQuantity,
                ApprovedQuantity = line.ApprovedQuantity,
                ConvertedQuantity = line.ConvertedQuantity,
                RemainingQuantity = (line.ApprovedQuantity ?? 0) - line.ConvertedQuantity,
                CurrentStockQuantity = line.ProductVariantId.HasValue
                    && line.ConversionFactor > 0
                        ? (_db.InventoryBalances
                            .Where(balance =>
                                balance.StoreId == storeId
                                && !balance.IsDeleted
                                && balance.ProductVariantId == line.ProductVariantId.Value)
                            .Sum(balance => (decimal?)balance.OnHandQty) ?? 0)
                            / line.ConversionFactor
                        : 0,
                IncomingQuantity = line.ProductVariantId.HasValue
                    && line.ConversionFactor > 0
                        ? (_db.PurchaseOrderLines
                            .Where(orderLine =>
                                orderLine.StoreId == storeId
                                && !orderLine.IsDeleted
                                && orderLine.ProductVariantId == line.ProductVariantId.Value
                                && orderLine.PurchaseOrder.StoreId == storeId
                                && !orderLine.PurchaseOrder.IsDeleted
                                && (orderLine.PurchaseOrder.Status == PurchaseOrderStatus.Approved
                                    || orderLine.PurchaseOrder.Status == PurchaseOrderStatus.SentToSupplier
                                    || orderLine.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived))
                            .Sum(orderLine => (decimal?)(
                                (orderLine.OrderedQuantity
                                    - orderLine.ReceivedQuantity
                                    - orderLine.ShortClosedQuantity)
                                * orderLine.ConversionFactor)) ?? 0)
                            / line.ConversionFactor
                        : 0
            })
            .ToListAsync(ct);

        foreach (var line in result.Lines)
        {
            line.RemainingQuantity = Math.Max(0, line.RemainingQuantity);
            line.IncomingQuantity = Math.Max(0, line.IncomingQuantity);
            line.ImageUrl = NormalizeImageUrl(line.ImageUrl);
        }

        return result;
    }

    private IQueryable<PurchaseRequest> BuildBaseQuery(int storeId)
        => _db.PurchaseRequests
            .AsNoTracking()
            .Where(item => item.StoreId == storeId && !item.IsDeleted);

    private IQueryable<PurchaseRequest> ApplyScope(
        IQueryable<PurchaseRequest> query,
        int storeId,
        int? requestedByUserId,
        PurchaseRequestIndexQueryRequest request)
    {
        if (requestedByUserId.HasValue)
        {
            query = query.Where(item =>
                item.RequestedByUserId == requestedByUserId.Value);
        }
        else if (request.RequesterUserId.HasValue)
        {
            query = query.Where(item =>
                item.RequestedByUserId == request.RequesterUserId.Value);
        }

        if (request.FromDate.HasValue)
        {
            var fromDate = request.FromDate.Value.Date;
            query = query.Where(item => item.RequestDate >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDateExclusive = request.ToDate.Value.Date.AddDays(1);
            query = query.Where(item => item.RequestDate < toDateExclusive);
        }

        return ApplyKeyword(query, storeId, request.Keyword);
    }

    private IQueryable<PurchaseRequest> ApplyKeyword(
        IQueryable<PurchaseRequest> query,
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
                item.RequestNumber.Contains(search)
                || EF.Functions.Collate(
                    item.Title.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch)
                || _db.Users.Any(user =>
                    user.Id == item.RequestedByUserId
                    && !user.IsDeleted
                    && (EF.Functions.Collate(
                            (user.FullName ?? string.Empty).Replace("Đ", "D").Replace("đ", "d"),
                            AccentInsensitiveSearchCollation)
                            .Contains(accentInsensitiveSearch)
                        || EF.Functions.Collate(
                            user.UserName.Replace("Đ", "D").Replace("đ", "d"),
                            AccentInsensitiveSearchCollation)
                            .Contains(accentInsensitiveSearch)))
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
            item.RequestNumber.Contains(search)
            || item.Title.Contains(search)
            || _db.Users.Any(user =>
                user.Id == item.RequestedByUserId
                && !user.IsDeleted
                && ((user.FullName != null && user.FullName.Contains(search))
                    || user.UserName.Contains(search)))
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

    private static IQueryable<PurchaseRequest> ApplyState(
        IQueryable<PurchaseRequest> query,
        string? state)
        => state switch
        {
            PurchaseRequestIndexStates.Working => query.Where(item =>
                item.Status == PurchaseRequestStatus.Draft
                || item.Status == PurchaseRequestStatus.ReturnedForRevision
                || item.Status == PurchaseRequestStatus.PendingApproval),
            PurchaseRequestIndexStates.OrderReady => query.Where(item =>
                item.Status == PurchaseRequestStatus.Approved
                || item.Status == PurchaseRequestStatus.PartiallyConverted),
            PurchaseRequestIndexStates.Converted => query.Where(item =>
                item.Status == PurchaseRequestStatus.Converted),
            PurchaseRequestIndexStates.Draft => query.Where(item =>
                item.Status == PurchaseRequestStatus.Draft),
            PurchaseRequestIndexStates.Pending => query.Where(item =>
                item.Status == PurchaseRequestStatus.PendingApproval),
            PurchaseRequestIndexStates.Returned => query.Where(item =>
                item.Status == PurchaseRequestStatus.ReturnedForRevision),
            PurchaseRequestIndexStates.Rejected => query.Where(item =>
                item.Status == PurchaseRequestStatus.Rejected),
            PurchaseRequestIndexStates.Approved => query.Where(item =>
                item.Status == PurchaseRequestStatus.Approved),
            PurchaseRequestIndexStates.Partial => query.Where(item =>
                item.Status == PurchaseRequestStatus.PartiallyConverted),
            PurchaseRequestIndexStates.Cancelled => query.Where(item =>
                item.Status == PurchaseRequestStatus.Cancelled),
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
