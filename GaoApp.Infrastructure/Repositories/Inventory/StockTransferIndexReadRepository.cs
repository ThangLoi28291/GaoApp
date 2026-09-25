using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class StockTransferIndexReadRepository : IStockTransferIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public StockTransferIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StockTransferIndexPageDto> QueryAsync(
        int storeId,
        StockTransferIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyScope(BuildBaseQuery(storeId), request);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new StockTransferIndexSummaryDto
            {
                TotalItems = group.Count(),
                WorkingItems = group.Count(document =>
                    document.Status == StockTransferDocumentStatus.Draft
                    || document.Status == StockTransferDocumentStatus.Rejected),
                PendingItems = group.Count(document =>
                    document.Status == StockTransferDocumentStatus.PendingApproval),
                ConfirmedItems = group.Count(document =>
                    document.Status == StockTransferDocumentStatus.Confirmed)
            })
            .FirstOrDefaultAsync(ct)
            ?? new StockTransferIndexSummaryDto();

        var filteredQuery = ApplyState(scopedQuery, request.State);
        var totalItems = await filteredQuery.CountAsync(ct);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling((double)totalItems / request.PageSize));
        var page = Math.Min(request.Page, totalPages);

        var items = await filteredQuery
            .OrderByDescending(document => document.DocumentDate)
            .ThenByDescending(document => document.Id)
            .Skip((page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(document => new StockTransferIndexItemDto
            {
                DocumentId = document.Id,
                DocumentNo = document.DocumentNo,
                DocumentDate = document.DocumentDate,
                FromWarehouseName = document.FromWarehouse.Name,
                ToWarehouseName = document.ToWarehouse.Name,
                Status = document.Status,
                TotalLines = document.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                TotalQuantity = document.Lines
                    .Where(line => line.StoreId == storeId && !line.IsDeleted)
                    .Sum(line => (decimal?)line.Quantity) ?? 0,
                Note = document.Note
            })
            .ToListAsync(ct);

        return new StockTransferIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    public async Task<StockTransferIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default)
    {
        var document = await BuildBaseQuery(storeId)
            .Where(item => item.Id == documentId)
            .Select(item => new StockTransferIndexQuickViewDto
            {
                DocumentId = item.Id,
                DocumentNo = item.DocumentNo,
                DocumentDate = item.DocumentDate,
                FromWarehouseName = item.FromWarehouse.Name,
                ToWarehouseName = item.ToWarehouse.Name,
                Status = item.Status,
                Note = item.Note,
                SubmittedAtUtc = item.SubmittedAtUtc,
                ApprovedAtUtc = item.ApprovedAtUtc,
                ConfirmedAtUtc = item.ConfirmedAtUtc,
                TotalLines = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                TotalQuantity = item.Lines
                    .Where(line => line.StoreId == storeId && !line.IsDeleted)
                    .Sum(line => (decimal?)line.Quantity) ?? 0
            })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return null;

        document.Lines = await _db.StockTransferLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && line.StockTransferDocumentId == documentId
                && line.ProductVariant.StoreId == storeId
                && !line.ProductVariant.IsDeleted
                && line.ProductVariant.Product.StoreId == storeId
                && !line.ProductVariant.Product.IsDeleted)
            .OrderBy(line => line.LineNo)
            .Select(line => new StockTransferIndexLineDto
            {
                LineNo = line.LineNo,
                ProductName = line.ProductNameSnapshot,
                Sku = line.SkuSnapshot,
                Barcode = line.BarcodeSnapshot,
                ImageUrl = line.ProductVariant.PrimaryProductImage != null
                    && !line.ProductVariant.PrimaryProductImage.IsDeleted
                        ? line.ProductVariant.PrimaryProductImage.MediaAsset.StoragePath
                        : line.ProductVariant.Product.ProductImages
                            .Where(image => !image.IsDeleted)
                            .OrderByDescending(image => image.IsPrimary)
                            .ThenBy(image => image.SortOrder)
                            .ThenBy(image => image.Id)
                            .Select(image => image.MediaAsset.StoragePath)
                            .FirstOrDefault(),
                UnitName = line.UnitNameSnapshot,
                Quantity = line.Quantity,
                BaseQuantity = line.BaseQuantity
            })
            .ToListAsync(ct);

        foreach (var line in document.Lines)
            line.ImageUrl = NormalizeImageUrl(line.ImageUrl);

        return document;
    }

    private IQueryable<StockTransferDocument> BuildBaseQuery(int storeId)
        => _db.StockTransferDocuments
            .AsNoTracking()
            .Where(document =>
                document.StoreId == storeId
                && !document.IsDeleted
                && document.FromWarehouse.StoreId == storeId
                && !document.FromWarehouse.IsDeleted
                && document.ToWarehouse.StoreId == storeId
                && !document.ToWarehouse.IsDeleted);

    private IQueryable<StockTransferDocument> ApplyScope(
        IQueryable<StockTransferDocument> query,
        StockTransferIndexQueryRequest request)
    {
        if (request.FromWarehouseId.HasValue)
        {
            query = query.Where(document =>
                document.FromWarehouseId == request.FromWarehouseId.Value);
        }

        if (request.ToWarehouseId.HasValue)
        {
            query = query.Where(document =>
                document.ToWarehouseId == request.ToWarehouseId.Value);
        }

        if (request.FromDate.HasValue)
        {
            var fromDate = request.FromDate.Value.Date;
            query = query.Where(document => document.DocumentDate >= fromDate);
        }

        if (request.ToDate.HasValue)
        {
            var toDateExclusive = request.ToDate.Value.Date.AddDays(1);
            query = query.Where(document => document.DocumentDate < toDateExclusive);
        }

        return ApplyKeyword(query, request.Keyword);
    }

    private IQueryable<StockTransferDocument> ApplyKeyword(
        IQueryable<StockTransferDocument> query,
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

            return query.Where(document =>
                document.DocumentNo.Contains(search)
                || (document.Note != null
                    && EF.Functions.Collate(
                        document.Note.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch))
                || EF.Functions.Collate(
                    document.FromWarehouse.Name.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch)
                || EF.Functions.Collate(
                    document.ToWarehouse.Name.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch));
        }

        return query.Where(document =>
            document.DocumentNo.Contains(search)
            || (document.Note != null && document.Note.Contains(search))
            || document.FromWarehouse.Name.Contains(search)
            || document.ToWarehouse.Name.Contains(search));
    }

    private static IQueryable<StockTransferDocument> ApplyState(
        IQueryable<StockTransferDocument> query,
        string? state)
        => state switch
        {
            StockTransferIndexStates.All => query,
            StockTransferIndexStates.Working => query.Where(document =>
                document.Status == StockTransferDocumentStatus.Draft
                || document.Status == StockTransferDocumentStatus.Rejected),
            StockTransferIndexStates.Draft => query.Where(document =>
                document.Status == StockTransferDocumentStatus.Draft),
            StockTransferIndexStates.Pending => query.Where(document =>
                document.Status == StockTransferDocumentStatus.PendingApproval),
            StockTransferIndexStates.Rejected => query.Where(document =>
                document.Status == StockTransferDocumentStatus.Rejected),
            StockTransferIndexStates.Confirmed => query.Where(document =>
                document.Status == StockTransferDocumentStatus.Confirmed),
            _ => query.Where(document =>
                document.Status == StockTransferDocumentStatus.Draft
                || document.Status == StockTransferDocumentStatus.PendingApproval)
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
