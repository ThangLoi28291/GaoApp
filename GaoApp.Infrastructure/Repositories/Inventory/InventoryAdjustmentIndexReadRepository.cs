using GaoApp.Application.DTOs.Inventory;
using GaoApp.Application.Interfaces.Repositories.Inventory;
using GaoApp.Domain.Entities;
using GaoApp.Domain.Enums;
using GaoApp.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GaoApp.Infrastructure.Repositories.Inventory;

public sealed class InventoryAdjustmentIndexReadRepository
    : IInventoryAdjustmentIndexReadRepository
{
    private const string AccentInsensitiveSearchCollation =
        "Latin1_General_100_CI_AI";

    private readonly AppDbContext _db;

    public InventoryAdjustmentIndexReadRepository(AppDbContext db)
    {
        _db = db;
    }

    public async Task<InventoryAdjustmentIndexPageDto> QueryAsync(
        int storeId,
        InventoryAdjustmentIndexQueryRequest request,
        CancellationToken ct = default)
    {
        var scopedQuery = ApplyScope(BuildBaseQuery(storeId), request);

        var summary = await scopedQuery
            .GroupBy(_ => 1)
            .Select(group => new InventoryAdjustmentIndexSummaryDto
            {
                TotalItems = group.Count(),
                WorkingItems = group.Count(document =>
                    document.Status == InventoryAdjustmentDocumentStatus.Draft
                    || document.Status == InventoryAdjustmentDocumentStatus.Rejected),
                PendingItems = group.Count(document =>
                    document.Status == InventoryAdjustmentDocumentStatus.PendingApproval),
                ApprovedItems = group.Count(document =>
                    document.Status == InventoryAdjustmentDocumentStatus.Approved)
            })
            .FirstOrDefaultAsync(ct)
            ?? new InventoryAdjustmentIndexSummaryDto();

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
            .Select(document => new InventoryAdjustmentIndexItemDto
            {
                DocumentId = document.Id,
                DocumentNo = document.DocumentNo,
                DocumentDate = document.DocumentDate,
                WarehouseName = document.Warehouse.Name,
                AdjustmentType = document.AdjustmentType,
                ReasonType = document.ReasonType,
                Status = document.Status,
                TotalLines = document.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted),
                Note = document.Note
            })
            .ToListAsync(ct);

        return new InventoryAdjustmentIndexPageDto
        {
            Page = page,
            PageSize = request.PageSize,
            TotalItems = totalItems,
            Summary = summary,
            Items = items
        };
    }

    public async Task<IReadOnlyList<InventoryAdjustmentIndexWarehouseOptionDto>> GetWarehouseOptionsAsync(
        int storeId,
        CancellationToken ct = default)
        => await BuildBaseQuery(storeId)
            .Select(document => new InventoryAdjustmentIndexWarehouseOptionDto
            {
                Id = document.WarehouseId,
                Name = document.Warehouse.Name
            })
            .Distinct()
            .OrderBy(item => item.Name)
            .ToListAsync(ct);

    public async Task<InventoryAdjustmentIndexQuickViewDto?> GetQuickViewAsync(
        int storeId,
        int documentId,
        CancellationToken ct = default)
    {
        var document = await BuildBaseQuery(storeId)
            .Where(item => item.Id == documentId)
            .Select(item => new InventoryAdjustmentIndexQuickViewDto
            {
                DocumentId = item.Id,
                DocumentNo = item.DocumentNo,
                DocumentDate = item.DocumentDate,
                WarehouseName = item.Warehouse.Name,
                AdjustmentType = item.AdjustmentType,
                ReasonType = item.ReasonType,
                Status = item.Status,
                Note = item.Note,
                ApprovalNote = item.ApprovalNote,
                SubmittedAtUtc = item.SubmittedAtUtc,
                ApprovedAtUtc = item.ApprovedAtUtc,
                RejectedAtUtc = item.RejectedAtUtc,
                CancelledAtUtc = item.CancelledAtUtc,
                TotalLines = item.Lines.Count(line =>
                    line.StoreId == storeId && !line.IsDeleted)
            })
            .FirstOrDefaultAsync(ct);

        if (document is null)
            return null;

        document.Lines = await _db.InventoryAdjustmentLines
            .AsNoTracking()
            .Where(line =>
                line.StoreId == storeId
                && !line.IsDeleted
                && line.InventoryAdjustmentDocumentId == documentId
                && line.ProductVariant.StoreId == storeId
                && !line.ProductVariant.IsDeleted
                && line.ProductVariant.Product.StoreId == storeId
                && !line.ProductVariant.Product.IsDeleted)
            .OrderBy(line => line.Id)
            .Select(line => new InventoryAdjustmentIndexLineDto
            {
                ProductName = line.ProductVariant.Product.Name,
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
                UnitName = line.Unit != null ? line.Unit.Name : string.Empty,
                Quantity = line.Quantity,
                BaseQuantity = line.BaseQuantity
            })
            .ToListAsync(ct);

        foreach (var line in document.Lines)
            line.ImageUrl = NormalizeImageUrl(line.ImageUrl);

        return document;
    }

    private IQueryable<InventoryAdjustmentDocument> BuildBaseQuery(int storeId)
        => _db.InventoryAdjustmentDocuments
            .AsNoTracking()
            .Where(document =>
                document.StoreId == storeId
                && !document.IsDeleted
                && document.Warehouse.StoreId == storeId
                && !document.Warehouse.IsDeleted);

    private IQueryable<InventoryAdjustmentDocument> ApplyScope(
        IQueryable<InventoryAdjustmentDocument> query,
        InventoryAdjustmentIndexQueryRequest request)
    {
        if (request.WarehouseId.HasValue)
        {
            query = query.Where(document =>
                document.WarehouseId == request.WarehouseId.Value);
        }

        if (request.AdjustmentType.HasValue)
        {
            query = query.Where(document =>
                document.AdjustmentType == request.AdjustmentType.Value);
        }

        if (request.ReasonType.HasValue)
        {
            query = query.Where(document =>
                document.ReasonType == request.ReasonType.Value);
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

    private IQueryable<InventoryAdjustmentDocument> ApplyKeyword(
        IQueryable<InventoryAdjustmentDocument> query,
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
                    document.Warehouse.Name.Replace("Đ", "D").Replace("đ", "d"),
                    AccentInsensitiveSearchCollation)
                    .Contains(accentInsensitiveSearch));
        }

        return query.Where(document =>
            document.DocumentNo.Contains(search)
            || (document.Note != null && document.Note.Contains(search))
            || document.Warehouse.Name.Contains(search));
    }

    private static IQueryable<InventoryAdjustmentDocument> ApplyState(
        IQueryable<InventoryAdjustmentDocument> query,
        string? state)
        => state switch
        {
            InventoryAdjustmentIndexStates.All => query,
            InventoryAdjustmentIndexStates.Working => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Draft
                || document.Status == InventoryAdjustmentDocumentStatus.Rejected),
            InventoryAdjustmentIndexStates.Draft => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Draft),
            InventoryAdjustmentIndexStates.Pending => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.PendingApproval),
            InventoryAdjustmentIndexStates.Rejected => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Rejected),
            InventoryAdjustmentIndexStates.Approved => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Approved),
            InventoryAdjustmentIndexStates.Cancelled => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Cancelled),
            _ => query.Where(document =>
                document.Status == InventoryAdjustmentDocumentStatus.Draft
                || document.Status == InventoryAdjustmentDocumentStatus.PendingApproval)
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
