using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class StockCountIndexReadRepository : IStockCountIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public StockCountIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<StockCountIndexPageDto> QueryAsync(
        int storeId,
        StockCountIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyScope(BuildBaseQuery(storeId), request);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new StockCountIndexSummaryDto
            {
                TotalItems = group.Count(),
                WorkingItems = group.Count(document =>
                    document.Status == StockCountDocumentStatus.Draft
                    || document.Status == StockCountDocumentStatus.Rejected),
                PendingItems = group.Count(document =>
                    document.Status == StockCountDocumentStatus.PendingApproval),
                ConfirmedItems = group.Count(document =>
                    document.Status == StockCountDocumentStatus.Confirmed)
            })
            .FirstOrDefaultAsync(ct)
            ?? new StockCountIndexSummaryDto();

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
            .Select(document => new StockCountIndexItemDto
            {
                DocumentId = document.Id,
                DocumentNo = document.DocumentNo,
                DocumentName = document.DocumentName,
                DocumentDate = document.DocumentDate,
                WarehouseName = document.Warehouse.Name,
                Status = document.Status,
                TotalLines = document.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                DifferenceLines = document.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.DifferenceQtyBase != 0),
                Note = document.Note
            })
            .ToListAsync(ct);

        return new StockCountIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    public async Task<IReadOnlyList<StockCountIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        int storeId,
        CancellationToken ct = default)
        => await BuildBaseQuery(storeId)
            .Select(document => new StockCountIndexWarehouseOptionDto
            {
                Id = document.WarehouseId,
                Name = document.Warehouse.Name
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .ToListAsync(ct);

    public async Task<StockCountIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default)
    {
        var document = await BuildBaseQuery(storeId)
            .Where(item => item.Id == documentId)
            .Select(item => new StockCountIndexQuickViewDto
            {
                DocumentId = item.Id,
                DocumentNo = item.DocumentNo,
                DocumentName = item.DocumentName,
                DocumentDate = item.DocumentDate,
                WarehouseName = item.Warehouse.Name,
                Status = item.Status,
                Note = item.Note,
                ConfirmedAtUtc = item.ConfirmedAtUtc,
                TotalLines = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                DifferenceLines = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.DifferenceQtyBase != 0),
                GainLines = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.DifferenceQtyBase > 0),
                LossLines = item.Lines.Count(line =>
                    line.StoreId == storeId
                    && !line.IsDeleted
                    && line.DifferenceQtyBase < 0)
            })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return null;

        document.Lines = await _db.StockCountLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && line.StockCountDocumentId == documentId)
            .OrderBy(line => line.LineNo)
            .Select(line => new StockCountIndexLineDto
            {
                LineNo = line.LineNo,
                ProductName = line.ProductNameSnapshot,
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
                UnitName = line.UnitNameSnapshot ?? line.Unit.Name,
                SystemQtyBase = line.SystemQtyBase,
                CountedQty = line.CountedQty,
                CountedQtyBase = line.CountedQtyBase,
                DifferenceQtyBase = line.DifferenceQtyBase
            })
            .ToListAsync(ct);

        foreach (var line in document.Lines)
            line.ImageUrl = NormalizeImageUrl(line.ImageUrl);

        return document;
    }

    private IQueryable<StockCountDocument> BuildBaseQuery(int storeId)
        => _db.StockCountDocuments
            .AsNoTracking()
            .Where(document =>
                document.StoreId == storeId
                && !document.IsDeleted
                && document.Warehouse.StoreId == storeId
                && !document.Warehouse.IsDeleted);

    private IQueryable<StockCountDocument> ApplyScope(
        IQueryable<StockCountDocument> query,
        StockCountIndexQueryRequest request)
    {
        if (request.WarehouseId.HasValue)
        {
            query = query.Where(document =>
                document.WarehouseId == request.WarehouseId.Value);
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

    private IQueryable<StockCountDocument> ApplyKeyword(
        IQueryable<StockCountDocument> query,
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
                || (document.DocumentName != null
                    && EF.Functions.Collate(
                        document.DocumentName.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch))
                || (document.Note != null
                    && EF.Functions.Collate(
                        document.Note.Replace("Đ", "D").Replace("đ", "d"),
                        AccentInsensitiveSearchCollation)
                        .Contains(accentInsensitiveSearch))
                || EF.Functions.Collate(
                    document.Warehouse.Name.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch));
        }

        return query.Where(document =>
            document.DocumentNo.Contains(search)
            || (document.DocumentName != null && document.DocumentName.Contains(search))
            || (document.Note != null && document.Note.Contains(search))
            || document.Warehouse.Name.Contains(search));
    }

    private static IQueryable<StockCountDocument> ApplyState(
        IQueryable<StockCountDocument> query,
        string? state)
        => state switch
        {
            StockCountIndexStates.All => query,
            StockCountIndexStates.Working => query.Where(document =>
                document.Status == StockCountDocumentStatus.Draft
                || document.Status == StockCountDocumentStatus.Rejected),
            StockCountIndexStates.Draft => query.Where(document =>
                document.Status == StockCountDocumentStatus.Draft),
            StockCountIndexStates.Pending => query.Where(document =>
                document.Status == StockCountDocumentStatus.PendingApproval),
            StockCountIndexStates.Rejected => query.Where(document =>
                document.Status == StockCountDocumentStatus.Rejected),
            StockCountIndexStates.Confirmed => query.Where(document =>
                document.Status == StockCountDocumentStatus.Confirmed),
            StockCountIndexStates.Cancelled => query.Where(document =>
                document.Status == StockCountDocumentStatus.Cancelled),
            _ => query.Where(document =>
                document.Status == StockCountDocumentStatus.Draft
                || document.Status == StockCountDocumentStatus.PendingApproval)
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
